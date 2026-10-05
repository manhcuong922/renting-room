using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

/// <param name="FinalReadings">Chỉ số cuối (ngày trả phòng) của từng công tơ — bắt buộc nếu chưa nhập; đã có thì bỏ trống = giữ, có số = sửa.</param>
public sealed record CreateFinalInvoiceCommand(Guid ContractId, IReadOnlyList<MeterReadingInput>? FinalReadings) : IRequest<Result<InvoiceDetailDto>>;

/// <summary>
/// BL-UC-11 / BL-BR-17: lập phiếu quyết toán (nháp, sửa được) cho HĐ đang thanh lý — kỳ [đầu kỳ cuối, ngày trả phòng], ghi chỉ số cuối
/// (MT-UC-05) trong cùng transaction. Chỉ thu phần còn thiếu; hoàn tiền: để sau. Còn phiếu nháp ⇒ chốt / xóa trước (BL-BR-18).
/// </summary>
public sealed class CreateFinalInvoiceHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CreateFinalInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(CreateFinalInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = await ContractMutation.LoadAsync(db, request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;
        if (contract.Status != ContractStatus.Liquidating)
            return ContractErrors.NotLiquidating;

        var invoices = await db.Invoices.AsNoTracking().Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void)
            .Select(i => new { i.Type, i.Status, i.PeriodStart, i.PeriodEnd }).ToListAsync(cancellationToken);
        if (invoices.Any(i => i.Type == InvoiceType.Final))
            return BillingErrors.FinalExists;
        if (invoices.Any(i => i.Status == InvoiceStatus.Draft))
            return BillingErrors.DraftExists;

        var period = contract.BillingPeriods(contract.ActualEndDate!.Value).Last();
        var previousBilled = invoices.Count == 0
            || invoices.Any(i => i.PeriodStart == period.Start || i.PeriodEnd == period.Start.AddDays(-1));
        if (!previousBilled && period.Start != contract.StartDate)
            return BillingErrors.PreviousNotBilled;

        var readings = await ContractMeterReadings.RecordAsync(
            db, contract, ReadingKind.Final, contract.ActualEndDate.Value, request.FinalReadings, cancellationToken);
        if (readings.IsFailure)
            return readings.Error!;
        await db.SaveChangesAsync(cancellationToken); // chỉ số cuối phải có trong DB trước khi nạp dữ liệu tính phiếu

        var calculation = InvoiceCalculator.Calculate(
            await InvoiceInputs.LoadAsync(db, contract, period, null, cancellationToken, InvoiceType.Final));
        var room = await db.Rooms.Where(r => r.Id == contract.RoomId).Select(r => r.Code).FirstAsync(cancellationToken);
        var representative = await db.Renters.Where(r => r.Id == contract.RepresentativeRenterId).Select(r => r.FullName).FirstAsync(cancellationToken);
        var invoice = Invoice.CreateDraft(contract.PropertyId, contract.RoomId, contract.Id, period.Start, period.End, room, contract.ContractNo,
            representative, calculation, InvoiceType.Final);
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(clock.GetUtcNow().ToBusinessDate());
    }
}

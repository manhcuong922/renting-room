using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
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
public sealed class CreateFinalInvoiceHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IInvoiceRepository invoiceRepository, IInvoiceQueries invoiceQueries,
    IPropertyQueries properties, IRoomQueries rooms, IRenterQueries renters, InvoiceInputLoader inputs, ContractMeterReadings meterReadings,
    RoomChargeBilling roomCharges, TimeProvider clock)
    : IRequestHandler<CreateFinalInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(CreateFinalInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = await contracts.GetForUpdateAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;
        if (contract.Status != ContractStatus.Liquidating)
            return ContractErrors.NotLiquidating;

        var invoices = await invoiceQueries.ListPeriodsAsync(contract.Id, cancellationToken);
        if (invoices.Any(i => i.Type == InvoiceType.Final))
            return BillingErrors.FinalExists;
        if (invoices.Any(i => i.Status == InvoiceStatus.Draft))
            return BillingErrors.DraftExists;

        var schedule = (await properties.GetBillingAsync(contract.PropertyId, cancellationToken)).Schedule;
        var period = contract.BillingPeriods(schedule, contract.ActualEndDate!.Value).Last();
        // BL-BR-21: các kỳ trước kỳ cuối phải có phiếu (kỳ cuối cũng là kỳ đầu ⇒ không cần).
        var previousBilled = invoices.Any(i => i.PeriodStart == period.Start || i.PeriodEnd == period.Start.AddDays(-1));
        if (!previousBilled && period.Start != contract.BillingStartDate)
            return BillingErrors.PreviousNotBilled;

        var readings = await meterReadings.RecordAsync(
            contract, ReadingKind.Final, contract.ActualEndDate.Value, request.FinalReadings, cancellationToken);
        if (readings.IsFailure)
            return readings.Error!;
        await unitOfWork.SaveChangesAsync(cancellationToken); // chỉ số cuối phải có trong DB trước khi nạp dữ liệu tính phiếu

        var calculation = InvoiceCalculator.Calculate(
            await inputs.LoadAsync(contract, period, null, cancellationToken, InvoiceType.Final));
        var room = (await rooms.GetCodeAsync(contract.RoomId, cancellationToken))!;
        var representative = (await renters.GetNamesAsync([contract.RepresentativeRenterId], cancellationToken))[contract.RepresentativeRenterId];
        var invoice = Invoice.CreateDraft(contract.PropertyId, contract.RoomId, contract.Id, period, room, contract.ContractNo,
            representative, calculation, InvoiceType.Final);
        invoiceRepository.Add(invoice);
        // BL-BR-32: mọi khoản phát sinh còn chờ của HĐ vào phiếu quyết toán.
        await roomCharges.AttachPendingAsync(invoice, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(clock.GetUtcNow().ToBusinessDate());
    }
}

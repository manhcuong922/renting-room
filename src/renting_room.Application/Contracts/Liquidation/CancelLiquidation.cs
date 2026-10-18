using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

/// <summary>Quay lại Active ⇒ ngày kết thúc thực tế bỏ trống; nếu phòng đã có HĐ mới sau đó, EXCLUDE trong DB trả 409.</summary>
public sealed record CancelLiquidationCommand(Guid ContractId) : IRequest<Result>;

/// <summary>
/// Hủy thanh lý (CT-UC-09): phiếu quyết toán đã chốt ⇒ phải hủy phiếu đó trước; nháp quyết toán bị xóa, chỉ số cuối bị hủy.
/// </summary>
public sealed class CancelLiquidationHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CancelLiquidationCommand, Result>
{
    public ValueTask<Result> Handle(CancelLiquidationCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var final = await db.Invoices.FirstOrDefaultAsync(i => i.ContractId == contract.Id && i.Type == InvoiceType.Final
                && i.Status != InvoiceStatus.Void, cancellationToken);
            if (final?.Status == InvoiceStatus.Finalized)
                return Result.Failure(BillingErrors.FinalFinalized);

            var cancelled = contract.CancelLiquidation();
            if (cancelled.IsFailure)
                return cancelled;
            // PM-BR-34: thanh lý để ký lại bị hủy ⇒ cọc không còn chuyển sang HĐ mới.
            contract.RevertDepositTransfer();
            if (final is not null)
                db.Invoices.Remove(final);
            await ContractMeterReadings.VoidFinalAsync(db, contract, clock.GetUtcNow(), cancellationToken);

            // CT-BR-31: người ở chưa ghi chuyển đi lại thành "đang ở" vô thời hạn — có thể đã chuyển sang phòng khác.
            var staying = contract.Occupants.Where(o => o.MoveOutDate is null).ToList();
            if (staying.Count == 0)
                return Result.Success();
            var people = await OccupantChecks.LoadPeopleAsync(db, staying.Select(o => o.RenterId), cancellationToken);
            var elsewhere = await OccupantChecks.FindLivingElsewhereAsync(db, contract,
                staying.Select(o => (o.RenterId, o.MoveInDate, (DateOnly?)null)).ToList(),
                people.ToDictionary(p => p.Key, p => p.Value.Name), cancellationToken);
            return elsewhere is null ? Result.Success() : Result.Failure(elsewhere);
        }, cancellationToken));
}

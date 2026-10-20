using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Quay lại Active ⇒ ngày kết thúc thực tế bỏ trống; nếu phòng đã có HĐ mới sau đó, EXCLUDE trong DB trả 409.</summary>
public sealed record CancelLiquidationCommand(Guid ContractId) : IRequest<Result<IReadOnlyList<Warning>>>;

public sealed class CancelLiquidationHandler(
    ContractMutation mutation, IContractRepository contracts, IInvoiceRepository invoices, ContractMeterReadings meterReadings,
    OccupantChecks occupantChecks, ResignVehicleRestorer vehicles, TimeProvider clock)
    : IRequestHandler<CancelLiquidationCommand, Result<IReadOnlyList<Warning>>>
{
    public ValueTask<Result<IReadOnlyList<Warning>>> Handle(CancelLiquidationCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync<IReadOnlyList<Warning>>(request.ContractId, async contract =>
        {
            var final = await invoices.FindFinalForUpdateAsync(contract.Id, cancellationToken);
            if (final?.Status == InvoiceStatus.Finalized)
                return BillingErrors.FinalFinalized;

            var cancelled = contract.CancelLiquidation();
            if (cancelled.IsFailure)
                return cancelled.Error!;
            contract.RevertDepositTransfer();
            if (final is not null)
                invoices.Remove(final);
            await meterReadings.VoidFinalAsync(contract, clock.GetUtcNow(), cancellationToken);

            var occupants = await CheckOccupantsAsync(contract, cancellationToken);
            if (occupants is not null)
                return occupants;

            var plates = contract.VehiclesEndedByResign().Select(v => v.PlateNumber).OfType<string>().ToList();
            if (plates.Count > 0 && await contracts.FindResignDraftAsync(contract.Id, cancellationToken) is { } draft)
                draft.ReleasePlates(plates);
            return Result.Success(await vehicles.RestoreAsync(contract, null, cancellationToken));
        }, cancellationToken));

    private async Task<Error?> CheckOccupantsAsync(Contract contract, CancellationToken ct)
    {
        var staying = contract.Occupants.Where(o => o.MoveOutDate is null).ToList();
        if (staying.Count == 0)
            return null;
        var people = await occupantChecks.LoadPeopleAsync(staying.Select(o => o.RenterId), ct);
        return await occupantChecks.FindLivingElsewhereAsync(contract,
            staying.Select(o => (o.RenterId, o.MoveInDate, (DateOnly?)null)).ToList(),
            people.ToDictionary(p => p.Key, p => p.Value.Name), ct);
    }
}

using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Fees;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

public sealed record ActivateContractCommand(
    Guid Id, IReadOnlyList<MeterReadingInput>? HandoverReadings = null) : IRequest<Result>;

/// <summary>
/// CT-BR-01/02/18/19. Khóa theo thứ tự rooms → contracts (C-07). Hai hợp đồng cùng phòng kích hoạt song song:
/// khóa hàng phòng tuần tự hóa, EXCLUDE constraint trong DB là chốt chặn cuối (→ 409 ROOM_PERIOD_OVERLAP).
/// </summary>
public sealed class ActivateContractHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IContractQueries contractQueries, IRoomQueries rooms, IPropertyQueries properties,
    IRenterQueries renters, IOrganizationQueries organizations, IFeeQueries fees, IMeterQueries meters, OccupantChecks occupantChecks,
    ContractMeterReadings meterReadings, TimeProvider clock)
    : IRequestHandler<ActivateContractCommand, Result>
{
    public async ValueTask<Result> Handle(ActivateContractCommand request, CancellationToken cancellationToken)
    {
        var roomId = await contractQueries.GetRoomIdAsync(request.Id, cancellationToken);
        if (roomId is null)
            return Result.Failure(ContractErrors.NotFound);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Room>(roomId.Value, cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(request.Id, cancellationToken);

        var contract = (await contracts.GetForUpdateAsync(request.Id, cancellationToken))!;
        var room = (await rooms.FindAsync(contract.RoomId, cancellationToken))!;
        var property = (await properties.GetAsync(contract.PropertyId, cancellationToken))!;
        var representative = (await renters.ListAsync([contract.RepresentativeRenterId], cancellationToken))[contract.RepresentativeRenterId];

        var occupantError = await CheckOccupantsAsync(contract, cancellationToken)
            ?? await occupantChecks.FindRoleConflictAsync(contract, cancellationToken);
        if (occupantError is not null)
            return Result.Failure(occupantError);
        if (await contractQueries.AnyRoomMoveFromAsync(contract.RoomId, contract.StartDate, cancellationToken))
            return Result.Failure(ContractErrors.RoomPeriodOverlap);

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        var (lessor, _) = await LessorSource.EffectiveAsync(organizations, property, cancellationToken);
        var feeTypes = await ContractFeeRules.LoadTypesAsync(fees, meters, contract, cancellationToken);
        var context = new ActivationContext(
            today,
            now,
            RoomAvailable: !room.IsArchived && !room.IsUnderMaintenance && !property.IsArchived,
            SigningSnapshotJson: lessor is null ? null : SigningSnapshot.From(property, lessor, room, representative, now).ToJson(),
            HouseRulesSnapshot: property.HouseRulesText,
            UtilityPriceSnapshotJson: UtilityPriceSnapshotJson.ToJson(UtilityPriceSnapshotJson.Capture(contract, feeTypes)));

        var activated = contract.Activate(context);
        if (activated.IsFailure)
            return activated;
        var handover = await meterReadings.RecordAsync(
            contract, ReadingKind.Handover, contract.BillingStartDate, request.HandoverReadings, cancellationToken);
        if (handover.IsFailure)
            return handover;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> CheckOccupantsAsync(Contract contract, CancellationToken ct)
    {
        var people = await occupantChecks.LoadPeopleAsync(
            contract.Occupants.Select(o => o.RenterId).Append(contract.ReferenceRenterId), ct);
        var occupants = contract.Occupants.ToList();
        return await occupantChecks.FindLivingElsewhereAsync(contract,
            occupants.Select(o => (o.RenterId, o.MoveInDate, o.MoveOutDate)).ToList(),
            people.ToDictionary(p => p.Key, p => p.Value.Name), ct);
    }
}

using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    public Result AddOccupant(OccupantInput input)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Active))
            return Result.Failure(ContractErrors.NotEditable);
        if (input.MoveInDate < StartDate)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (Status == ContractStatus.Draft && EndDate is { } end && input.MoveInDate > end)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (_occupants.Any(o => o.RenterId == input.RenterId && o.OverlapsFrom(input.MoveInDate)))
            return Result.Failure(ContractErrors.OccupantOverlap);

        _occupants.Add(new ContractOccupant(Id, WithRole(input)));
        BillingChanged();
        return Result.Success();
    }

    private OccupantInput WithRole(OccupantInput input) =>
        input.RenterId == RepresentativeRenterId ? input with { Role = OccupantRole.Resident } : input;

    public Result ChangeOccupantRole(Guid occupantId, OccupantRole role)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Active))
            return Result.Failure(ContractErrors.NotEditable);
        var occupant = _occupants.FirstOrDefault(o => o.Id == occupantId);
        if (occupant is null)
            return Result.Failure(ContractErrors.OccupantNotFound);
        if (occupant.MoveOutDate is not null)
            return Result.Failure(ContractErrors.OccupantAlreadyMovedOut);
        if (occupant.RenterId == RepresentativeRenterId)
            return Result.Failure(ContractErrors.OccupantIsRepresentative);

        BillingChangedIf(occupant.SetRole(role));
        return Result.Success();
    }

    public Result ChangeRepresentative(
        Guid newRepresentativeId, DateOnly date, DateOnly today, Guid? householdHeadRenterId, bool oldRepresentativeMovesOut, string? signingSnapshotJson)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (newRepresentativeId == RepresentativeRenterId)
            return Result.Failure(ContractErrors.RepresentativeUnchanged);
        var since = RepresentativeSince ?? StartDate;
        if (date > today || date < since)
            return Result.Failure(ContractErrors.RepresentativeInvalidDate);
        var head = householdHeadRenterId ?? HouseholdHeadRenterId;
        if (householdHeadRenterId is { } chosen && !_occupants.Any(o => o.RenterId == chosen && o.OverlapsFrom(date)))
            return Result.Failure(ContractErrors.HouseholdHeadNotOccupant);

        var (oldRepresentativeId, oldReference) = (RepresentativeRenterId, ReferenceRenterId);
        _representativeHistory.Add(new ContractRepresentative(Id, oldRepresentativeId, since, date));
        RepresentativeRenterId = newRepresentativeId;
        RepresentativeSince = date;
        HouseholdHeadRenterId = head == newRepresentativeId ? null : head;
        HasSignedDocument = false;
        SignedDocumentNote = null;
        SigningSnapshot = signingSnapshotJson ?? SigningSnapshot;

        var staying = _occupants.Where(o => o.MoveOutDate is null).ToList();
        foreach (var own in staying.Where(o => o.RenterId == newRepresentativeId))
            BillingChangedIf(own.SetRole(OccupantRole.Resident));
        if (ReferenceRenterId != oldReference)
            staying.ForEach(o => BillingChangedIf(o.ClearRelationship()));
        if (!oldRepresentativeMovesOut)
            return Result.Success();

        foreach (var leaving in staying.Where(o => o.RenterId == oldRepresentativeId && o.MoveInDate <= date))
            BillingChangedIf(leaving.MoveOut(date));
        foreach (var vehicle in _vehicles.Where(v => v.IsActive && v.RenterId == oldRepresentativeId && v.RegisteredFrom <= date))
            vehicle.End(date);
        return Result.Success();
    }

    public Result EndOccupancy(Guid occupantId, DateOnly moveOutDate)
    {
        if (Status is not (ContractStatus.Active or ContractStatus.Liquidating))
            return Result.Failure(ContractErrors.NotActive);

        var occupant = _occupants.FirstOrDefault(o => o.Id == occupantId);
        if (occupant is null)
            return Result.Failure(ContractErrors.OccupantNotFound);
        if (occupant.MoveOutDate is not null)
            return Result.Failure(ContractErrors.OccupantAlreadyMovedOut);
        if (moveOutDate < occupant.MoveInDate || (ActualEndDate is { } actualEnd && moveOutDate > actualEnd))
            return Result.Failure(ContractErrors.DateOutsideContract);

        BillingChangedIf(occupant.MoveOut(moveOutDate));
        return Result.Success();
    }
}

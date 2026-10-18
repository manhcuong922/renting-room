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
        // CT-BR-37: HĐ đang hiệu lực quá hạn mà vẫn ở = không thời hạn ⇒ chỉ nháp mới bị ngày hết hạn chặn.
        if (Status == ContractStatus.Draft && EndDate is { } end && input.MoveInDate > end)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (_occupants.Any(o => o.RenterId == input.RenterId && o.OverlapsFrom(input.MoveInDate)))
            return Result.Failure(ContractErrors.OccupantOverlap);

        _occupants.Add(new ContractOccupant(Id, WithRole(input)));
        return Result.Success();
    }

    /// <summary>CT-BR-49: người đứng tên không mang vai trò đồng thuê.</summary>
    private OccupantInput WithRole(OccupantInput input) =>
        input.RenterId == RepresentativeRenterId ? input with { Role = OccupantRole.Resident } : input;

    /// <summary>CT-BR-49: đổi vai trò người ở ↔ đồng thuê (HĐ nháp / đang hiệu lực, người chưa chuyển đi).</summary>
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

        occupant.SetRole(role);
        return Result.Success();
    }

    /// <summary>
    /// CT-BR-51: đổi người đứng tên, HĐ giữ nguyên (số HĐ, nợ, cọc, khoản phát sinh, chuỗi chỉ số). Bản ký cũ không còn đúng người ⇒ về
    /// "Thiếu tài liệu"; mốc khai quan hệ đổi ⇒ xóa quan hệ người ở để khai lại.
    /// </summary>
    /// <param name="signingSnapshotJson">Bản chụp lúc ký đã thay phần bên thuê (null = HĐ chưa có bản chụp).</param>
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
            own.SetRole(OccupantRole.Resident);
        if (ReferenceRenterId != oldReference)
            staying.ForEach(o => o.ClearRelationship());
        if (!oldRepresentativeMovesOut)
            return Result.Success();

        foreach (var leaving in staying.Where(o => o.RenterId == oldRepresentativeId && o.MoveInDate <= date))
            leaving.MoveOut(date);
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

        occupant.MoveOut(moveOutDate);
        return Result.Success();
    }
}

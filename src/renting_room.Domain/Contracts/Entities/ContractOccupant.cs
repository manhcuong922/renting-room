using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed class ContractOccupant : TenantEntity
{
    private ContractOccupant() { } // EF Core

    internal ContractOccupant(Guid contractId, OccupantInput input)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        RenterId = input.RenterId;
        MoveInDate = input.MoveInDate;
        ExpectedEndDate = input.ExpectedEndDate;
        Relationship = TextNormalizer.TrimToNull(input.Relationship);
        RelationshipType = input.RelationshipType;
        GuardianConsent = input.GuardianConsent;
        Note = TextNormalizer.TrimToNull(input.Note);
        Role = input.Role;
    }

    public Guid ContractId { get; private set; }
    public Guid RenterId { get; private set; }

    public OccupantRole Role { get; private set; }
    public DateOnly MoveInDate { get; private set; }
    public DateOnly? MoveOutDate { get; private set; }
    public DateOnly? ExpectedEndDate { get; private set; }
    public string? Relationship { get; private set; }

    public OccupantRelationship? RelationshipType { get; private set; }

    public bool GuardianConsent { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Dạng <see cref="OccupantInput"/> để kiểm tra quan hệ cùng người mới (ngày ra thực tế thay cho dự kiến).</summary>
    public OccupantInput ToInput() =>
        new(RenterId, MoveInDate, MoveOutDate ?? ExpectedEndDate, Relationship, Note, RelationshipType, GuardianConsent, Role);

    public bool IsStayingOn(DateOnly date) => MoveInDate <= date && (MoveOutDate is null || MoveOutDate >= date);

    public bool OverlapsFrom(DateOnly from) => MoveOutDate is null || MoveOutDate >= from;

    internal bool MoveOut(DateOnly date)
    {
        if (MoveOutDate == date)
            return false;
        MoveOutDate = date;
        return true;
    }

    internal bool SetRole(OccupantRole role)
    {
        if (Role == role)
            return false;
        Role = role;
        return true;
    }

    internal bool ClearRelationship()
    {
        if (RelationshipType is null && Relationship is null)
            return false;
        RelationshipType = null;
        Relationship = null;
        return true;
    }
}

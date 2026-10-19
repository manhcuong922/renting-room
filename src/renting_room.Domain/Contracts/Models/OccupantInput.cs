namespace renting_room.Domain.Contracts;

/// <param name="Relationship">Ghi chú quan hệ (bắt buộc khi <see cref="RelationshipType"/> = Other).</param>
/// <param name="GuardianConsent">Người chưa thành niên: đã có ý kiến đồng ý của cha, mẹ hoặc người giám hộ (Luật Cư trú Điều 28).</param>
public sealed record OccupantInput(
    Guid RenterId,
    DateOnly MoveInDate,
    DateOnly? ExpectedEndDate,
    string? Relationship,
    string? Note,
    OccupantRelationship? RelationshipType = null,
    bool GuardianConsent = false,
    OccupantRole Role = OccupantRole.Resident);

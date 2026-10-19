using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

/// <summary>Phụ thu (người thuê trả thêm) hoặc bù (giảm trừ cho người thuê).</summary>
public enum RoomChargeKind
{
    Surcharge,
    Credit
}

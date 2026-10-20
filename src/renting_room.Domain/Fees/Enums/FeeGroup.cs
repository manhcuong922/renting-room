using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

public enum FeeGroup
{
    Metered,  // Điện nước: theo công tơ của phòng — (mới − cũ) × một đơn giá
    Service   // Dịch vụ: gắn HĐ, tính theo ChargeBasis
}

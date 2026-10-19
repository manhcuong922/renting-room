using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

/// <summary>Nhóm khoản thu (FE-BR-02: bất biến sau khi tạo). Phụ thu không thuộc danh mục — nhập trên phiếu (M07).</summary>
public enum FeeGroup
{
    Metered,  // Điện nước: theo công tơ của phòng — (mới − cũ) × một đơn giá
    Service   // Dịch vụ: gắn HĐ, tính theo ChargeBasis
}

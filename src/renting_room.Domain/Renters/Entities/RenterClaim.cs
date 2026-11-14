using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

/// <summary>
/// M01 ID-BR-26 (08/11/2026): quản lý khu <see cref="UserId"/> nhập đúng số giấy tờ của hồ sơ ngoài phạm vi khu (người thuê cũ của khu khác quay lại thuê)
/// ⇒ được thấy và dùng lại hồ sơ đó. Hợp đồng / lịch sử ở khu khác vẫn ẩn.
/// </summary>
public sealed class RenterClaim : TenantEntity
{
    private RenterClaim() { } // EF Core

    public Guid RenterId { get; private set; }
    public Guid UserId { get; private set; }

    public static RenterClaim Create(Guid renterId, Guid userId) => new() { Id = Guid.CreateVersion7(), RenterId = renterId, UserId = userId };
}

using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

/// <summary>M01 ID-BR-24 (08/11/2026): quản lý khu <see cref="UserId"/> được giao khu <see cref="PropertyId"/>. Đổi giao khu = thay cả tập.</summary>
public sealed class UserPropertyAccess : TenantEntity
{
    private UserPropertyAccess() { } // EF Core

    public Guid UserId { get; private set; }
    public Guid PropertyId { get; private set; }

    public static UserPropertyAccess Grant(Guid userId, Guid propertyId) =>
        new() { Id = Guid.CreateVersion7(), UserId = userId, PropertyId = propertyId };
}

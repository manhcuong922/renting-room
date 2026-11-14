namespace renting_room.Application.Identity;

/// <summary>M01 ID-BR-24 (08/11/2026): khu được giao cho quản lý khu.</summary>
public interface IPropertyAccessRepository
{
    /// <summary>Khu được giao của các user (không có dòng ⇒ không có khóa trong kết quả).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Thay toàn bộ khu được giao của <paramref name="userId"/> (gọi SaveChanges sau).</summary>
    Task ReplaceAsync(Guid userId, IReadOnlyCollection<Guid> propertyIds, CancellationToken cancellationToken);

    /// <summary>Khu đang dùng (chưa ngừng) của tổ chức hiện tại trong <paramref name="propertyIds"/>; null ⇒ mọi khu đang dùng.</summary>
    Task<IReadOnlyList<Guid>> ListActivePropertyIdsAsync(IReadOnlyCollection<Guid>? propertyIds, CancellationToken cancellationToken);
}

/// <summary>Phạm vi khu của một user đọc ở mỗi request (có cache ngắn) — xóa cache khi chủ trọ đổi giao khu ⇒ có hiệu lực ngay.</summary>
public interface IPropertyScopeStore
{
    /// <summary>null ⇒ không giới hạn khu (không phải quản lý khu).</summary>
    ValueTask<Guid[]?> GetAsync(Guid userId, CancellationToken cancellationToken);

    void Invalidate(Guid userId);
}

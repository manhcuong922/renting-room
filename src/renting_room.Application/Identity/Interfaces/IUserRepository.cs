using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

/// <summary>
/// Lấy / cất tài khoản. Các lệnh cập nhật trực tiếp (không đọc-sửa-ghi) dành cho chỗ nhiều request song song cùng tác động
/// một tài khoản — chạy ngay, không chờ <see cref="Common.Interfaces.IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tài khoản thuộc tổ chức (null = không thuộc tổ chức nào).</summary>
    Task<User?> GetInOrganizationForUpdateAsync(Guid id, Guid? organizationId, CancellationToken cancellationToken);

    Task<User?> GetNonAdminForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsPhoneTakenAsync(string phoneNormalized, Guid? excludeId, CancellationToken cancellationToken);

    Task<bool> IsEmailTakenAsync(string emailNormalized, Guid? excludeId, CancellationToken cancellationToken);

    void Add(User user);

    /// <summary>
    /// Đếm lần nhập sai mật khẩu (đăng nhập và đổi mật khẩu dùng chung bộ đếm — ID-BR-06) bằng UPDATE nguyên tử để nhiều request sai
    /// song song không làm lệch bộ đếm. Đủ ngưỡng → khóa tạm và đặt lại bộ đếm.
    /// </summary>
    Task RegisterFailedLoginAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Đăng nhập đúng: xóa bộ đếm sai, gỡ khóa tạm, ghi lần đăng nhập cuối.</summary>
    Task RecordSuccessfulLoginAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Chỉ ghi hash mới nếu hash đang lưu vẫn là <paramref name="verifiedHash"/> — không đè mật khẩu vừa đổi bởi request khác.</summary>
    Task UpgradePasswordHashAsync(Guid userId, string verifiedHash, string upgradedHash, CancellationToken cancellationToken);

    /// <summary>Đổi security stamp ⇒ access token đang lưu hành hết hiệu lực.</summary>
    Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken);

    Task RotateSecurityStampsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);
}

namespace renting_room.Application.Common.Interfaces;

/// <summary>
/// Kiểm tra phiên của access token còn hợp lệ không (user chưa bị khóa, tổ chức chưa bị tạm ngưng,
/// security stamp chưa đổi). Kết quả được cache ngắn hạn để không truy vấn DB ở mọi request.
/// </summary>
public interface IUserSessionStore
{
    ValueTask<bool> IsValidAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken);

    /// <summary>Xóa cache sau khi đổi mật khẩu / khóa / tạm ngưng để áp dụng ngay trên instance hiện tại.</summary>
    void Invalidate(Guid userId);
}

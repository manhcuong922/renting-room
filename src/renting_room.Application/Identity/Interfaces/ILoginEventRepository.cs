using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

public interface ILoginEventRepository
{
    void Add(LoginEvent loginEvent);

    /// <summary>Đếm lần sai gần đây của tài khoản theo IP đang thử (ID-BR-06) — <paramref name="notBefore"/>: lần đổi / cấp lại mật khẩu.</summary>
    Task<LoginFailureStats> GetFailureStatsAsync(
        Guid userId, string? ipAddress, DateTimeOffset now, DateTimeOffset? notBefore, CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

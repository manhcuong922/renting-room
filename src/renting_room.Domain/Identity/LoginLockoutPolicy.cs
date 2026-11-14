namespace renting_room.Domain.Identity;

/// <summary>Số lần đăng nhập sai gần đây của một tài khoản, nhìn từ IP đang thử (đếm từ <c>login_events</c>).</summary>
/// <param name="FailuresFromIp">Sai từ chính IP này — sau lần đúng gần nhất từ IP này, sau lần đổi / cấp lại mật khẩu, trong <see cref="LoginLockoutPolicy.IpWindow"/>.</param>
/// <param name="IsKnownIp">IP đã đăng nhập đúng tài khoản này trong <see cref="LoginLockoutPolicy.KnownIpLookback"/>.</param>
/// <param name="FailuresFromUnknownIps">Tổng lần sai từ các IP lạ trong <see cref="LoginLockoutPolicy.UnknownIpWindow"/> (sau lần đổi mật khẩu).</param>
public sealed record LoginFailureStats(int FailuresFromIp, bool IsKnownIp, int FailuresFromUnknownIps);

/// <summary>
/// M01 ID-BR-06 (đổi 05/11/2026, L-13): khóa đăng nhập theo IP + tài khoản thay vì khóa cả tài khoản — gõ sai cố ý chỉ khóa được IP của
/// người gõ; dò bằng nhiều IP lạ chạm trần chung, IP quen của chủ tài khoản vẫn vào được.
/// </summary>
public static class LoginLockoutPolicy
{
    public const int MaxFailuresPerIp = 5;
    public static readonly TimeSpan IpWindow = TimeSpan.FromMinutes(15);
    public const int MaxFailuresFromUnknownIps = 30;
    public static readonly TimeSpan UnknownIpWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan KnownIpLookback = TimeSpan.FromDays(90);

    public static bool IsBlocked(LoginFailureStats stats) =>
        stats.FailuresFromIp >= MaxFailuresPerIp
        || (!stats.IsKnownIp && stats.FailuresFromUnknownIps >= MaxFailuresFromUnknownIps);
}

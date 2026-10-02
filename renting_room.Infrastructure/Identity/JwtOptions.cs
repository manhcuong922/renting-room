using System.ComponentModel.DataAnnotations;

namespace renting_room.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSigningKeyBytes = 32; // HS256 yêu cầu khóa ≥ 256 bit

    [Required]
    public string Issuer { get; init; } = "renting_room";

    [Required]
    public string Audience { get; init; } = "renting_room_api";

    /// <summary>
    /// Khóa ký HS256 (≥ 32 byte UTF-8). Production: bắt buộc, đặt qua biến môi trường / secret manager.
    /// Development: để trống ⇒ sinh khóa ngẫu nhiên mỗi lần chạy (token mất hiệu lực khi restart).
    /// </summary>
    public string? SigningKey { get; init; }

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 30;

    /// <summary>Hạn tuyệt đối của một phiên đăng nhập (≥ RefreshTokenDays).</summary>
    [Range(1, 365)]
    public int SessionAbsoluteDays { get; init; } = 90;

    /// <summary>Thời gian cache kết quả kiểm tra phiên (stamp, khóa, tạm ngưng). 0 = không cache.</summary>
    [Range(0, 300)]
    public int SessionCacheSeconds { get; init; } = 60;
}

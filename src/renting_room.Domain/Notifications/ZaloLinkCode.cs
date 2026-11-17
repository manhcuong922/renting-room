using System.Security.Cryptography;
using renting_room.Domain.Common;

namespace renting_room.Domain.Notifications;

/// <summary>
/// NT-BR-13: mã dùng một lần gắn một cuộc chat Zalo với một hồ sơ người thuê. DB chỉ lưu hash (HMAC có khóa — L-19), không lưu mã thô.
/// </summary>
public sealed class ZaloLinkCode : TenantEntity
{
    /// <summary>L-19: rút từ 7 ngày của thiết kế gốc xuống 48 giờ — mã sống càng lâu càng dễ bị dò / lộ.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

    public const int Length = 8;

    /// <summary>Bỏ <c>0 O 1 I L</c> cho khỏi nhầm khi đọc / gõ.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private ZaloLinkCode() { } // EF Core

    public Guid RenterId { get; private set; }
    public string CodeHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <param name="hash">Hàm băm mã đã chuẩn hóa (HMAC có khóa ở Infrastructure).</param>
    /// <returns>Entity (chỉ có hash) và mã thô — mã thô chỉ trả cho người tạo đúng một lần.</returns>
    public static (ZaloLinkCode Entity, string Code) Create(Guid renterId, DateTimeOffset now, Func<string, string> hash)
    {
        var code = string.Create(Length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
        var entity = new ZaloLinkCode
        {
            Id = Guid.CreateVersion7(), RenterId = renterId, CodeHash = hash(Normalize(code)), ExpiresAt = now + Lifetime
        };
        return (entity, code);
    }

    /// <summary>Không phân biệt hoa thường, bỏ khoảng trắng và dấu gạch — người thuê gõ tay trên điện thoại.</summary>
    public static string Normalize(string input) =>
        new(input.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && now < ExpiresAt;

    /// <summary>Dùng một lần: lần thứ hai trả false.</summary>
    public bool MarkUsed(DateTimeOffset now)
    {
        if (UsedAt is not null)
            return false;
        UsedAt = now;
        return true;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

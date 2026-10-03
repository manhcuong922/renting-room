using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Identity;

public sealed class PersonalDataOptions
{
    public const string SectionName = "PersonalData";
    public const int MinHashKeyLength = 32;

    /// <summary>
    /// Khóa HMAC cho hash số giấy tờ. PHẢI cố định suốt vòng đời hệ thống (đổi khóa ⇒ không tìm / chống trùng được dữ liệu cũ).
    /// Đặt qua user-secrets (dev) hoặc secret manager (prod) — không ghi vào appsettings.
    /// </summary>
    [Required(ErrorMessage = "PersonalData:HashKey is not configured. Dev: dotnet user-secrets set \"PersonalData:HashKey\" \"<>=32 random chars>\".")]
    [MinLength(MinHashKeyLength)]
    public string HashKey { get; init; } = null!;
}

/// <summary>
/// Mã hóa số giấy tờ bằng ASP.NET Data Protection (AES, khóa xoay vòng tự động, đọc được dữ liệu cũ)
/// + HMAC-SHA256 để tìm kiếm chính xác / unique mà không lưu giá trị rõ (C-11).
/// </summary>
public sealed class PersonalDataProtector(IDataProtectionProvider dataProtection, IOptions<PersonalDataOptions> options)
    : IPersonalDataProtector
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("renting_room.PersonalData.v1");
    private readonly byte[] _hashKey = Encoding.UTF8.GetBytes(options.Value.HashKey);

    public byte[] Encrypt(string value) => _protector.Protect(Encoding.UTF8.GetBytes(value));

    public string Decrypt(byte[] protectedValue) => Encoding.UTF8.GetString(_protector.Unprotect(protectedValue));

    public string Hash(Guid organizationId, string normalizedValue) =>
        Convert.ToHexString(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes($"{organizationId:N}:{normalizedValue}")))
            .ToLowerInvariant();
}

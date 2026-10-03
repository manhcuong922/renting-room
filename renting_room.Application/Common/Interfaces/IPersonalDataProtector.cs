using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Common.Interfaces;

/// <summary>Bảo vệ dữ liệu cá nhân (C-11): mã hóa để đọc lại được, HMAC để tìm kiếm / chống trùng mà không lộ giá trị.</summary>
public interface IPersonalDataProtector
{
    byte[] Encrypt(string value);

    string Decrypt(byte[] protectedValue);

    /// <summary>HMAC-SHA256 theo tổ chức — cùng số giấy tờ ở 2 tổ chức cho 2 hash khác nhau.</summary>
    string Hash(Guid organizationId, string normalizedValue);
}

public static class PersonalDataProtectorExtensions
{
    public static ProtectedIdNumber ProtectIdNumber(
        this IPersonalDataProtector protector, Guid organizationId, IdDocumentType type, string rawNumber)
    {
        var normalized = IdDocumentNumber.Normalize(rawNumber);
        return new ProtectedIdNumber(
            type,
            protector.Encrypt(normalized),
            protector.Hash(organizationId, $"{type}:{normalized}"),
            IdDocumentNumber.LastFour(normalized));
    }

    public static string Mask(string? last4) => last4 is null ? string.Empty : $"********{last4}";
}

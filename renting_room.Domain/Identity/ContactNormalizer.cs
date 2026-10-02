using System.Text.RegularExpressions;

namespace renting_room.Domain.Identity;

/// <summary>Chuẩn hóa SĐT Việt Nam / email về một dạng duy nhất trước khi lưu, so sánh và đánh unique index.</summary>
public static partial class ContactNormalizer
{
    [GeneratedRegex(@"^0(3|5|7|8|9)\d{8}$")]
    private static partial Regex VietnamMobileRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    /// <summary>"+84 912.345.678", "84912345678", "0912-345-678" → "0912345678"; không hợp lệ → null.</summary>
    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());

        if (digits.StartsWith("+84", StringComparison.Ordinal))
            digits = "0" + digits[3..];
        else if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
            digits = "0" + digits[2..];

        return VietnamMobileRegex().IsMatch(digits) ? digits : null;
    }

    /// <summary>Trim + lowercase; không đúng định dạng hoặc quá 254 ký tự → null.</summary>
    public static string? NormalizeEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var email = raw.Trim().ToLowerInvariant();
        return email.Length <= 254 && EmailRegex().IsMatch(email) ? email : null;
    }
}

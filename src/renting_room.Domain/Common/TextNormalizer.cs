using System.Globalization;
using System.Text;

namespace renting_room.Domain.Common;

public static class TextNormalizer
{
    /// <summary>"Trần Thị Lan" → "tran thi lan" — để tìm kiếm không dấu, không phân biệt hoa thường.</summary>
    public static string ToSearchText(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Mã nghiệp vụ (mã khu, mã phòng, số hợp đồng): trim + chữ hoa ⇒ unique không phân biệt hoa thường.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

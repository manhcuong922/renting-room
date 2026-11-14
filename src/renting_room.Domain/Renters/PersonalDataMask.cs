using System.Text;

namespace renting_room.Domain.Renters;

/// <summary>
/// RT-BR-06 (05/11/2026, L-17): mẫu tìm họ tên / SĐT / email / số giấy tờ của một người trong ô ghi chú tự do để che bằng <see cref="Mask"/>.
/// Chỉ dùng cú pháp chung của regex .NET và PostgreSQL (<c>regexp_replace(…, 'gi')</c>): <c>(?:…)</c>, <c>[\s.\-]</c>, ký tự thoát bằng <c>\</c>.
/// Số (SĐT, giấy tờ) bắt cả cách viết có dấu cách / chấm / gạch giữa các chữ số; SĐT bắt thêm dạng +84 / 84. Giới hạn: không bắt cách viết khác
/// của tên (thiếu họ, viết tắt, không dấu).
/// </summary>
public static class PersonalDataMask
{
    public const string Mask = "***";
    private const string Separator = @"[\s.\-]?";

    /// <returns>null khi không có gì để tìm.</returns>
    public static string? Pattern(IEnumerable<string?> identifiers)
    {
        var parts = identifiers
            .Select(i => i?.Trim())
            .Where(i => !string.IsNullOrEmpty(i) && i.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(i => IsNumber(i!) ? NumberPattern(i!) : Escape(i!))
            .OrderByDescending(p => p.Length) // mẫu dài trước: không để phần ngắn che dở phần dài
            .ToList();
        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    private static bool IsNumber(string value) =>
        value.Count(char.IsAsciiDigit) >= 6 && value.All(c => char.IsAsciiDigit(c) || c is ' ' or '.' or '-' or '+');

    private static string NumberPattern(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        // SĐT Việt Nam 0xxxxxxxxx: số 0 đầu có thể viết thành +84 / 84.
        if (digits.Length == 10 && digits[0] == '0')
            return @"(?:0|\+?84" + Separator + ")" + string.Join(Separator, digits[1..].ToCharArray());
        return string.Join(Separator, digits.ToCharArray());
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length * 2);
        foreach (var c in value)
        {
            if (@"\^$.|?*+()[]{}".Contains(c))
                builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }
}

using System.Text;
using System.Xml;

namespace renting_room.Application.Common.Formatting;

/// <summary>
/// M-25: chữ người dùng nhập / dán từ Word chứa ký tự điều khiển (Shift+Enter = U+000B, NUL = U+0000) làm PostgreSQL từ chối (22021)
/// hoặc làm bản in Word / XML ném lỗi. Chuẩn hóa một chỗ, dùng cho cả đầu vào JSON lẫn bộ ghi Word.
/// </summary>
public static class TextSanitizer
{
    /// <summary>Bỏ ký tự điều khiển trừ <c>\n \r \t</c>; U+000B / U+000C (xuống dòng mềm / sang trang) ⇒ <c>\n</c>; bỏ surrogate lẻ.</summary>
    public static string Normalize(string value)
    {
        if (!NeedsNormalization(value))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\u000B' or '\u000C')
                builder.Append('\n');
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                builder.Append(c).Append(value[++i]);
            else if (char.IsSurrogate(c) || (char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                continue;
            else
                builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>Lọc ký tự không hợp lệ trong XML 1.0 (dữ liệu đã lưu từ trước vẫn in được); xuống dòng mềm ⇒ khoảng trắng.</summary>
    public static string ForXml(string value)
    {
        if (value.All(XmlConvert.IsXmlChar) && !value.Any(c => c is '\u000B' or '\u000C'))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\u000B' or '\u000C')
                builder.Append(' ');
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
                builder.Append(c).Append(value[++i]);
            else if (XmlConvert.IsXmlChar(c))
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool NeedsNormalization(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }
            if (char.IsSurrogate(c) || (char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                return true;
        }
        return false;
    }
}

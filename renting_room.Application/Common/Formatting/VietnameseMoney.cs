using System.Globalization;
using System.Text;

namespace renting_room.Application.Common.Formatting;

/// <summary>Định dạng tiền cho văn bản: "3.500.000 đ" và "Ba triệu năm trăm nghìn đồng".</summary>
public static class VietnameseMoney
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Scales = ["", " nghìn", " triệu", " tỷ", " nghìn tỷ", " triệu tỷ"];

    /// <summary>"3.500.000 đ"; đơn giá có phần lẻ giữ tối đa 2 số ("15.500,5 đ").</summary>
    public static string Format(decimal amount) => amount.ToString("#,##0.##", Vietnamese) + " đ";

    /// <summary>Số nguyên không âm → chữ, viết hoa chữ đầu, kết thúc "đồng". Phần lẻ dưới 1 đồng bị bỏ.</summary>
    public static string ToWords(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");

        var value = (long)decimal.Truncate(amount);
        if (value == 0)
            return "Không đồng";

        var groups = new List<int>();
        for (var v = value; v > 0; v /= 1000)
            groups.Add((int)(v % 1000));

        var words = new StringBuilder();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0)
                continue;
            var isLeading = i == groups.Count - 1;
            words.Append(' ').Append(ReadGroup(groups[i], isLeading)).Append(Scales[i]);
        }

        var text = words.ToString().Trim();
        return char.ToUpper(text[0], Vietnamese) + text[1..] + " đồng";
    }

    /// <summary>Đọc 3 chữ số. Nhóm không đứng đầu luôn đọc đủ hàng trăm ("không trăm năm mươi").</summary>
    private static string ReadGroup(int number, bool isLeading)
    {
        int hundreds = number / 100, tens = number / 10 % 10, units = number % 10;
        var parts = new List<string>();

        if (hundreds > 0 || !isLeading)
            parts.Add($"{Digits[hundreds]} trăm");

        if (tens > 1)
            parts.Add($"{Digits[tens]} mươi");
        else if (tens == 1)
            parts.Add("mười");
        else if (units > 0 && parts.Count > 0)
            parts.Add("lẻ");

        if (units > 0)
            parts.Add(units switch
            {
                1 when tens > 1 => "mốt",
                5 when tens > 0 => "lăm",
                _ => Digits[units]
            });

        return string.Join(' ', parts);
    }
}

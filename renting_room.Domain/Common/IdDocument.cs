using System.Text.RegularExpressions;

namespace renting_room.Domain.Common;

public enum IdDocumentType
{
    /// <summary>CCCD / thẻ căn cước — 12 chữ số.</summary>
    CitizenId,

    /// <summary>CMND 9 số — chỉ để lưu dữ liệu cũ.</summary>
    LegacyId,

    Passport
}

public static partial class IdDocumentNumber
{
    [GeneratedRegex(@"^\d{12}$")]
    private static partial Regex CitizenIdRegex();

    [GeneratedRegex(@"^\d{9}$")]
    private static partial Regex LegacyIdRegex();

    [GeneratedRegex(@"^[A-Z0-9]{6,20}$")]
    private static partial Regex PassportRegex();

    /// <summary>Một hàm chuẩn hóa duy nhất trước khi mã hóa / băm: bỏ ký tự không phải chữ-số, chữ hoa.</summary>
    public static string Normalize(string raw) =>
        new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static bool IsValid(IdDocumentType type, string normalized) => type switch
    {
        IdDocumentType.CitizenId => CitizenIdRegex().IsMatch(normalized),
        IdDocumentType.LegacyId => LegacyIdRegex().IsMatch(normalized),
        IdDocumentType.Passport => PassportRegex().IsMatch(normalized),
        _ => false
    };

    public static string LastFour(string normalized) => normalized.Length <= 4 ? normalized : normalized[^4..];
}

namespace renting_room.Domain.Attachments;

/// <summary>Loại file nhận diện bằng magic bytes (FS-BR-01) — không tin Content-Type / đuôi file.</summary>
public sealed record DetectedFileType(string ContentType, bool IsImage, bool IsJpeg);

public static class FileSignature
{
    /// <summary>Số byte đầu cần đọc để nhận diện.</summary>
    public const int HeaderLength = 32;

    private static readonly DetectedFileType Jpeg = new("image/jpeg", true, true);
    private static readonly DetectedFileType Png = new("image/png", true, false);
    private static readonly DetectedFileType Webp = new("image/webp", true, false);
    private static readonly DetectedFileType Heic = new("image/heic", true, false);
    private static readonly DetectedFileType Pdf = new("application/pdf", false, false);

    private static readonly string[] HeicBrands = ["heic", "heix", "heim", "heis", "hevc", "hevx", "mif1", "msf1"];

    /// <returns>Loại được phép (JPEG, PNG, WEBP, HEIC, PDF) hoặc null.</returns>
    public static DetectedFileType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return Jpeg;
        if (StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Png;
        if (StartsWith(header, "%PDF-"u8))
            return Pdf;
        if (header.Length >= 12 && StartsWith(header, "RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return Webp;
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8)
            && HeicBrands.Contains(System.Text.Encoding.ASCII.GetString(header[8..12])))
            return Heic;
        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) => data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}

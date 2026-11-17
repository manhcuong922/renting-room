using System.IO.Compression;
using System.Text;

namespace renting_room.Infrastructure.Seeding;

/// <summary>Ảnh PNG / PDF mẫu sinh bằng code (không kèm file nhị phân trong repo) cho dữ liệu demo M09.</summary>
internal static class DemoSampleFiles
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>PNG màu gradient <paramref name="width"/> × <paramref name="height"/> — hiện rõ trên màn hình để thấy ảnh đã tải đúng.</summary>
    public static byte[] Png(int width, int height, byte red, byte green, byte blue)
    {
        var stride = 1 + width * 3;
        var raw = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * stride; // byte đầu mỗi dòng = bộ lọc 0
            var shade = 1.0 - 0.45 * y / height;
            for (var x = 0; x < width; x++)
            {
                raw[row + 1 + x * 3] = (byte)(red * shade);
                raw[row + 2 + x * 3] = (byte)(green * shade);
                raw[row + 3 + x * 3] = (byte)(blue * shade);
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; // 8 bit
        header[9] = 2; // RGB
        WriteChunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>PDF 1 trang tối thiểu, đủ để trình đọc mở được.</summary>
    public static byte[] Pdf(string title)
    {
        var text = title.Replace("\\", string.Empty, StringComparison.Ordinal).Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);
        var stream = $"BT /F1 18 Tf 72 720 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        ];
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            builder.Append($"{offset:D10} 00000 n \n");
        builder.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        output.Write(length);
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
        data.CopyTo(typeAndData, 4);
        output.Write(typeAndData);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeAndData));
        output.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}

namespace renting_room.Application.Attachments;

/// <summary>FS-BR-10: bỏ metadata EXIF (có tọa độ GPS) khỏi ảnh JPEG trước khi lưu. Ảnh khác giữ nguyên (PNG / WEBP / HEIC chưa xử lý — P2).</summary>
public static class JpegMetadata
{
    /// <summary>Bỏ các đoạn APP1 (EXIF / XMP) và APP13 (IPTC); giữ nguyên mọi đoạn còn lại, gồm cả ICC (APP2). Dữ liệu không hợp lệ ⇒ trả nguyên bản.</summary>
    public static byte[] StripExif(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return jpeg;

        var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2);
        var position = 2;
        while (position + 4 <= jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
                return jpeg;
            var marker = jpeg[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            // SOS (0xDA): từ đây là dữ liệu ảnh nén tới hết file — chép nguyên.
            if (marker == 0xDA || marker == 0xD9)
            {
                output.Write(jpeg, position, jpeg.Length - position);
                return output.ToArray();
            }

            var length = (jpeg[position + 2] << 8) | jpeg[position + 3];
            if (length < 2 || position + 2 + length > jpeg.Length)
                return jpeg;
            if (marker is not (0xE1 or 0xED))
                output.Write(jpeg, position, 2 + length);
            position += 2 + length;
        }

        return jpeg;
    }
}

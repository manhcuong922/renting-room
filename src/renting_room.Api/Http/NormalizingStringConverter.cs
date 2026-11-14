using System.Text.Json;
using System.Text.Json.Serialization;
using renting_room.Application.Common.Formatting;

namespace renting_room.Http;

/// <summary>M-25: mọi chuỗi trong JSON đầu vào được chuẩn hóa ký tự điều khiển trước khi vào validator / DB. Chiều ghi giữ nguyên.</summary>
internal sealed class NormalizingStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } value ? TextSanitizer.Normalize(value) : null;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

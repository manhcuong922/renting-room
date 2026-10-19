namespace renting_room.Domain.Contracts;

/// <summary>Định nghĩa trường tùy biến: hợp đồng dùng mẫu sẽ nhập giá trị theo <see cref="Key"/>.</summary>
public sealed record CustomFieldDefinition(
    string Key,
    string Label,
    CustomFieldType Type,
    bool Required,
    IReadOnlyList<string>? Options,
    string? Unit,
    string? Hint);

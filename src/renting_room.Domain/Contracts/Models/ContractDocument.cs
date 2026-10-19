namespace renting_room.Domain.Contracts;

/// <summary>
/// Phần văn bản của hợp đồng (CT-BR-25): loại, tiêu đề, điều khoản, trường tùy biến — chép từ mẫu lúc tạo/sửa nháp.
/// Các chuỗi JSON đã được Application kiểm tra (định nghĩa trường + giá trị khớp định nghĩa).
/// </summary>
public sealed record ContractDocument(
    Guid? TemplateId,
    ContractType ContractType,
    string Title,
    string? ClausesJson,
    string? FieldDefinitionsJson,
    string? FieldValuesJson)
{
    public static ContractDocument Default(ContractType type = ContractType.RoomRental) =>
        new(null, type, ContractTypes.DefaultTitle(type), null, null, null);
}

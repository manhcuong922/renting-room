using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>Loại hợp đồng — quyết định tiêu đề mặc định và bộ mẫu gợi ý.</summary>
public enum ContractType
{
    RoomRental,       // Thuê phòng trọ
    WholeHouseRental  // Thuê nhà nguyên căn
}

/// <summary>Kiểu dữ liệu của trường tùy biến trong mẫu hợp đồng.</summary>
public enum CustomFieldType
{
    Text,      // ≤ 500 ký tự
    LongText,  // ≤ 5000 ký tự
    Number,
    Money,     // VND, số nguyên
    Date,      // yyyy-MM-dd
    Boolean,
    Select     // một giá trị trong Options
}

/// <summary>Một điều khoản (mục) của hợp đồng, VD "Trách nhiệm của bên A".</summary>
public sealed record ContractClause(string Heading, string Body);

/// <summary>Định nghĩa trường tùy biến: hợp đồng dùng mẫu sẽ nhập giá trị theo <see cref="Key"/>.</summary>
public sealed record CustomFieldDefinition(
    string Key,
    string Label,
    CustomFieldType Type,
    bool Required,
    IReadOnlyList<string>? Options,
    string? Unit,
    string? Hint);

public static class ContractTypes
{
    public static string DefaultTitle(ContractType type) => type switch
    {
        ContractType.WholeHouseRental => "HỢP ĐỒNG THUÊ NHÀ",
        _ => "HỢP ĐỒNG THUÊ PHÒNG TRỌ"
    };
}

/// <summary>
/// Mẫu hợp đồng của tổ chức (CT-BR-25): tiêu đề, các điều khoản, và danh sách trường tùy biến.
/// Hợp đồng nháp chép nội dung mẫu vào chính nó ⇒ sửa / ngừng dùng mẫu không đổi hợp đồng đã kích hoạt.
/// Điều khoản và trường lưu dạng JSON đã được Application kiểm tra.
/// </summary>
public sealed class ContractTemplate : TenantEntity
{
    private ContractTemplate() { } // EF Core

    public string Name { get; private set; } = null!;
    public ContractType ContractType { get; private set; }
    public string Title { get; private set; } = null!;
    public string ClausesJson { get; private set; } = "[]";
    public string FieldDefinitionsJson { get; private set; } = "[]";

    /// <summary>CT-BR-27: hợp đồng dùng mẫu này không đặt cọc (tiền cọc luôn = 0).</summary>
    public bool NoDeposit { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static ContractTemplate Create(
        string name, ContractType type, string title, string clausesJson, string fieldDefinitionsJson, bool noDeposit)
    {
        var template = new ContractTemplate { Id = Guid.CreateVersion7() };
        template.Update(name, type, title, clausesJson, fieldDefinitionsJson, noDeposit);
        return template;
    }

    public void Update(string name, ContractType type, string title, string clausesJson, string fieldDefinitionsJson, bool noDeposit)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Template name and title are required.");

        Name = name.Trim();
        ContractType = type;
        Title = title.Trim();
        ClausesJson = clausesJson;
        FieldDefinitionsJson = fieldDefinitionsJson;
        NoDeposit = noDeposit;
    }

    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
            return Result.Failure(ContractTemplateErrors.AlreadyArchived);

        ArchivedAt = now;
        return Result.Success();
    }

    public Result Restore()
    {
        if (!IsArchived)
            return Result.Failure(ContractTemplateErrors.NotArchived);

        ArchivedAt = null;
        return Result.Success();
    }
}

public static class ContractTemplateErrors
{
    public static readonly Error NotFound = Error.NotFound("CONTRACT_TEMPLATE_NOT_FOUND", "Không tìm thấy mẫu hợp đồng.");
    public static readonly Error NameTaken = Error.Conflict("CONTRACT_TEMPLATE_NAME_TAKEN", "Tên mẫu hợp đồng đã tồn tại.");
    public static readonly Error Archived = Error.BusinessRule("CONTRACT_TEMPLATE_ARCHIVED",
        "Mẫu hợp đồng đã ngừng sử dụng — chọn mẫu khác hoặc khôi phục mẫu.");
    public static readonly Error AlreadyArchived = Error.Conflict("CONTRACT_TEMPLATE_ALREADY_ARCHIVED", "Mẫu hợp đồng đã ngừng sử dụng.");
    public static readonly Error NotArchived = Error.Conflict("CONTRACT_TEMPLATE_NOT_ARCHIVED", "Mẫu hợp đồng đang được sử dụng.");
    public static readonly Error DepositNotAllowed = Error.Validation("DEPOSIT_NOT_ALLOWED",
        "Mẫu hợp đồng không cọc — tiền cọc phải bằng 0.");
}

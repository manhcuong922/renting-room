using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

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

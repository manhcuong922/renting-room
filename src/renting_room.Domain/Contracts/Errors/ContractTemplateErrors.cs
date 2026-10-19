using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

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

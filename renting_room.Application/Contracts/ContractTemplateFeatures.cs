using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ContractTemplateInput(
    string Name,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause>? Clauses,
    IReadOnlyList<CustomFieldDefinition>? Fields,
    bool NoDeposit = false);

public sealed record ContractTemplateDto(
    Guid Id,
    string Name,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause> Clauses,
    IReadOnlyList<CustomFieldDefinition> Fields,
    bool NoDeposit,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    string Version)
{
    public static ContractTemplateDto From(ContractTemplate t) => new(
        t.Id, t.Name, t.ContractType, t.Title,
        ContractDocumentJson.Clauses(t.ClausesJson), ContractDocumentJson.Fields(t.FieldDefinitionsJson),
        t.NoDeposit, t.IsArchived, t.CreatedAt, t.Version.ToString());
}

internal sealed class ContractTemplateInputValidator : AbstractValidator<ContractTemplateInput>
{
    public ContractTemplateInputValidator()
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên mẫu");
        RuleFor(x => x.ContractType).IsInEnum();
        RuleFor(x => x.Title).RequiredText(200, "Tiêu đề hợp đồng");
        this.ClausesRules(x => x.Clauses, "clauses");
        this.FieldsRules(x => x.Fields, "fields");
    }
}

// ============================================================ Truy vấn

public sealed record ListContractTemplatesQuery(ContractType? Type, bool IncludeArchived) : IRequest<IReadOnlyList<ContractTemplateDto>>;

public sealed class ListContractTemplatesHandler(IAppDbContext db) : IRequestHandler<ListContractTemplatesQuery, IReadOnlyList<ContractTemplateDto>>
{
    public async ValueTask<IReadOnlyList<ContractTemplateDto>> Handle(ListContractTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = db.ContractTemplates.AsNoTracking();
        if (request.Type is { } type)
            query = query.Where(t => t.ContractType == type);
        if (!request.IncludeArchived)
            query = query.Where(t => t.ArchivedAt == null);

        var templates = await query.OrderBy(t => t.ContractType).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        return templates.Select(ContractTemplateDto.From).ToList();
    }
}

public sealed record GetContractTemplateQuery(Guid Id) : IRequest<Result<ContractTemplateDto>>;

public sealed class GetContractTemplateHandler(IAppDbContext db) : IRequestHandler<GetContractTemplateQuery, Result<ContractTemplateDto>>
{
    public async ValueTask<Result<ContractTemplateDto>> Handle(GetContractTemplateQuery request, CancellationToken cancellationToken)
    {
        var template = await db.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        return template is null ? ContractTemplateErrors.NotFound : ContractTemplateDto.From(template);
    }
}

/// <summary>Mẫu gợi ý dựng sẵn trong code — UI dùng làm điểm xuất phát rồi POST để tạo mẫu của tổ chức.</summary>
public sealed record GetContractTemplatePresetsQuery : IRequest<IReadOnlyList<ContractTemplateInput>>;

public sealed class GetContractTemplatePresetsHandler : IRequestHandler<GetContractTemplatePresetsQuery, IReadOnlyList<ContractTemplateInput>>
{
    public ValueTask<IReadOnlyList<ContractTemplateInput>> Handle(GetContractTemplatePresetsQuery request, CancellationToken cancellationToken) =>
        new(ContractTemplatePresets.All);
}

// ============================================================ Tạo / sửa / ngừng dùng

public sealed record CreateContractTemplateCommand(ContractTemplateInput Template) : IRequest<Result<Guid>>;

public sealed class CreateContractTemplateCommandValidator : AbstractValidator<CreateContractTemplateCommand>
{
    // Body gửi phẳng ⇒ key lỗi không có tiền tố "template.".
    public CreateContractTemplateCommandValidator()
    {
        RuleFor(x => x.Template).NotNull();
        RuleFor(x => x.Template).FlattenedValidator(new ContractTemplateInputValidator());
    }
}

public sealed class CreateContractTemplateHandler(IAppDbContext db) : IRequestHandler<CreateContractTemplateCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateContractTemplateCommand request, CancellationToken cancellationToken)
    {
        var input = request.Template;
        var name = input.Name.Trim();
        if (await db.ContractTemplates.AnyAsync(t => t.Name == name, cancellationToken))
            return ContractTemplateErrors.NameTaken;

        var template = ContractTemplate.Create(name, input.ContractType, input.Title,
            ContractDocumentJson.ToJson(input.Clauses) ?? "[]", ContractDocumentJson.ToJson(input.Fields) ?? "[]", input.NoDeposit);
        db.ContractTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return template.Id;
    }
}

public sealed record UpdateContractTemplateCommand(Guid Id, ContractTemplateInput Template, uint Version) : IRequest<Result<ContractTemplateDto>>;

public sealed class UpdateContractTemplateCommandValidator : AbstractValidator<UpdateContractTemplateCommand>
{
    public UpdateContractTemplateCommandValidator()
    {
        RuleFor(x => x.Template).NotNull();
        RuleFor(x => x.Template).FlattenedValidator(new ContractTemplateInputValidator());
    }
}

/// <summary>CT-BR-25: sửa mẫu không đổi hợp đồng đã kích hoạt; bản nháp nhận nội dung mới ở lần sửa nháp kế tiếp.</summary>
public sealed class UpdateContractTemplateHandler(IAppDbContext db) : IRequestHandler<UpdateContractTemplateCommand, Result<ContractTemplateDto>>
{
    public async ValueTask<Result<ContractTemplateDto>> Handle(UpdateContractTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (template is null)
            return ContractTemplateErrors.NotFound;

        var input = request.Template;
        var name = input.Name.Trim();
        if (await db.ContractTemplates.AnyAsync(t => t.Name == name && t.Id != template.Id, cancellationToken))
            return ContractTemplateErrors.NameTaken;

        db.SetExpectedVersion(template, request.Version);
        template.Update(name, input.ContractType, input.Title,
            ContractDocumentJson.ToJson(input.Clauses) ?? "[]", ContractDocumentJson.ToJson(input.Fields) ?? "[]", input.NoDeposit);
        await db.SaveChangesAsync(cancellationToken);
        return ContractTemplateDto.From(template);
    }
}

public sealed record ChangeContractTemplateStateCommand(Guid Id, bool Archive) : IRequest<Result>;

public sealed class ChangeContractTemplateStateHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ChangeContractTemplateStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeContractTemplateStateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.ContractTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (template is null)
            return Result.Failure(ContractTemplateErrors.NotFound);

        var result = request.Archive ? template.Archive(clock.GetUtcNow()) : template.Restore();
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================ Mẫu gợi ý

/// <summary>
/// Điểm xuất phát cho chủ trọ. Điều khoản theo mẫu thực tế + các nội dung Luật Nhà ở 2023 Điều 163 yêu cầu
/// (bàn giao, quyền và nghĩa vụ, chấm dứt, giải quyết tranh chấp). Trường tùy biến chỉ để GHI NHẬN thỏa thuận
/// trên hợp đồng — tính tiền điện/nước tự động thuộc module khoản thu (M04).
/// </summary>
public static class ContractTemplatePresets
{
    private static readonly string[] PaymentTimes = ["Đầu tháng", "Cuối tháng"];

    private static readonly CustomFieldDefinition[] UtilityFields =
    [
        new("electricity_pricing", "Cách tính tiền điện", CustomFieldType.Select, true,
            ["Theo giá nhà nước (bậc thang EVN)", "Đơn giá cố định theo kWh"], null, "Tính theo chỉ số công tơ"),
        new("electricity_unit_price", "Đơn giá điện (nếu giá cố định)", CustomFieldType.Money, false, null, "đ/kWh", null),
        new("electricity_payment_time", "Thời điểm trả tiền điện", CustomFieldType.Select, false, PaymentTimes, null, null),
        new("water_pricing", "Cách tính tiền nước", CustomFieldType.Select, true,
            ["Theo đầu người", "Theo khối (m³)", "Trọn gói theo phòng"], null, null),
        new("water_unit_price", "Đơn giá nước", CustomFieldType.Money, true, null, "đ", "VD 20.000đ/người hoặc đ/m³"),
        new("water_payment_time", "Thời điểm trả tiền nước", CustomFieldType.Select, false, PaymentTimes, null, null),
        new("wifi_included", "Có cung cấp wifi", CustomFieldType.Boolean, false, null, null, null),
        new("other_services", "Dịch vụ khác (rác, giữ xe…)", CustomFieldType.LongText, false, null, null, "Ghi rõ đơn giá từng dịch vụ")
    ];

    private static readonly ContractClause DisputeClause = new("Giải quyết tranh chấp",
        "- Tranh chấp phát sinh được hai bên thương lượng, hòa giải trên tinh thần hợp tác.\n" +
        "- Không thương lượng được thì một trong hai bên có quyền yêu cầu Tòa án có thẩm quyền giải quyết theo quy định của pháp luật.");

    public static readonly ContractTemplateInput RoomRental = new(
        "Thuê phòng trọ (mẫu chuẩn)",
        ContractType.RoomRental,
        ContractTypes.DefaultTitle(ContractType.RoomRental),
        [
            new("Trách nhiệm của bên A",
                "- Bàn giao phòng và trang thiết bị cho bên B đúng thời hạn, đúng hiện trạng đã thỏa thuận.\n" +
                "- Tạo mọi điều kiện thuận lợi để bên B thực hiện theo hợp đồng.\n" +
                "- Cung cấp nguồn điện, nước (và các dịch vụ đã thỏa thuận) cho bên B sử dụng."),
            new("Trách nhiệm của bên B",
                "- Thanh toán đầy đủ các khoản tiền theo đúng thỏa thuận.\n" +
                "- Bảo quản các trang thiết bị và cơ sở vật chất bên A trang bị ban đầu (làm hỏng phải sửa, mất phải đền).\n" +
                "- Không được tự ý sửa chữa, cải tạo cơ sở vật chất khi chưa được sự đồng ý của bên A.\n" +
                "- Giữ gìn vệ sinh trong và ngoài khuôn viên phòng trọ.\n" +
                "- Chấp hành quy định của pháp luật và của địa phương; thực hiện đăng ký tạm trú theo quy định.\n" +
                "- Cho khách ở qua đêm phải báo và được bên A đồng ý, đồng thời chịu trách nhiệm về hành vi vi phạm pháp luật của khách."),
            new("Trách nhiệm chung",
                "- Hai bên tạo điều kiện cho nhau thực hiện hợp đồng.\n" +
                "- Bên nào vi phạm điều khoản đã thỏa thuận thì bên còn lại có quyền đơn phương chấm dứt hợp đồng; vi phạm gây tổn thất thì bên vi phạm phải bồi thường.\n" +
                "- Bên muốn chấm dứt hợp đồng trước thời hạn phải báo trước cho bên kia theo số ngày đã thỏa thuận.\n" +
                "- Khi chấm dứt hợp đồng, bên A hoàn trả tiền đặt cọc cho bên B sau khi trừ các khoản bên B còn nợ hoặc phải bồi thường (nếu có)."),
            DisputeClause
        ],
        UtilityFields);

    public static readonly ContractTemplateInput WholeHouseRental = new(
        "Thuê nhà nguyên căn (mẫu chuẩn)",
        ContractType.WholeHouseRental,
        ContractTypes.DefaultTitle(ContractType.WholeHouseRental),
        [
            new("Đặc điểm nhà cho thuê",
                "Bên A cho bên B thuê toàn bộ ngôi nhà tại địa chỉ ghi trong hợp đồng, gồm diện tích, số tầng, " +
                "giấy tờ sở hữu và mục đích sử dụng như các thông tin bổ sung của hợp đồng này."),
            new("Trách nhiệm của bên A",
                "- Bàn giao nhà và trang thiết bị đúng thời hạn, đúng hiện trạng.\n" +
                "- Bảo đảm quyền sử dụng ổn định của bên B trong thời hạn thuê.\n" +
                "- Sửa chữa hư hỏng thuộc kết cấu chính của ngôi nhà không do lỗi của bên B."),
            new("Trách nhiệm của bên B",
                "- Sử dụng nhà đúng mục đích đã thỏa thuận; thanh toán tiền thuê và chi phí điện, nước, dịch vụ đúng hạn.\n" +
                "- Không cho thuê lại, chuyển nhượng hợp đồng khi chưa có sự đồng ý bằng văn bản của bên A.\n" +
                "- Không tự ý đục phá, cải tạo, phá dỡ nhà.\n" +
                "- Chấp hành quy định về phòng cháy chữa cháy, an ninh trật tự, vệ sinh môi trường; đăng ký tạm trú theo quy định.\n" +
                "- Trả lại nhà và trang thiết bị khi hết hạn hợp đồng."),
            new("Chấm dứt hợp đồng",
                "- Hợp đồng chấm dứt khi hết hạn mà không gia hạn, hoặc hai bên thỏa thuận chấm dứt.\n" +
                "- Một bên đơn phương chấm dứt phải báo trước cho bên kia theo số ngày đã thỏa thuận; trường hợp đơn phương chấm dứt theo Luật Nhà ở 2023 Điều 172.\n" +
                "- Bên vi phạm gây thiệt hại phải bồi thường."),
            DisputeClause
        ],
        [
            new("land_area_m2", "Diện tích đất", CustomFieldType.Number, false, null, "m²", null),
            new("floor_area_m2", "Tổng diện tích sàn sử dụng", CustomFieldType.Number, true, null, "m²", null),
            new("floor_count", "Số tầng", CustomFieldType.Number, true, null, "tầng", null),
            new("ownership_document", "Giấy tờ sở hữu nhà", CustomFieldType.Text, false, null, null, "Số giấy chứng nhận, ngày cấp"),
            new("usage_purpose", "Mục đích sử dụng", CustomFieldType.Select, true, ["Để ở", "Kinh doanh", "Để ở kết hợp kinh doanh"], null, null),
            new("rent_payment_cycle", "Kỳ trả tiền thuê", CustomFieldType.Select, true, ["Hằng tháng", "3 tháng/lần", "6 tháng/lần", "12 tháng/lần"], null, null),
            new("sublease_allowed", "Cho phép cho thuê lại", CustomFieldType.Boolean, false, null, null, null),
            .. UtilityFields
        ]);

    /// <summary>CT-BR-27: không đặt cọc ⇒ bù rủi ro bằng trả tiền thuê trước và quyền chấm dứt khi chậm trả.</summary>
    public static readonly ContractTemplateInput RoomRentalNoDeposit = new(
        "Thuê phòng trọ không cọc (mẫu chuẩn)",
        ContractType.RoomRental,
        ContractTypes.DefaultTitle(ContractType.RoomRental),
        [
            RoomRental.Clauses![0],
            RoomRental.Clauses![1],
            new("Thanh toán (không đặt cọc)",
                "- Hai bên thỏa thuận không đặt cọc.\n" +
                "- Bên B thanh toán tiền thuê trước, vào đầu mỗi kỳ thu.\n" +
                "- Bên B chậm thanh toán quá số ngày đã thỏa thuận thì bên A có quyền đơn phương chấm dứt hợp đồng; " +
                "bên B phải thanh toán đủ các khoản còn nợ và bồi thường thiệt hại đối với tài sản (nếu có)."),
            new("Trách nhiệm chung",
                "- Hai bên tạo điều kiện cho nhau thực hiện hợp đồng.\n" +
                "- Bên nào vi phạm điều khoản đã thỏa thuận thì bên còn lại có quyền đơn phương chấm dứt hợp đồng; vi phạm gây tổn thất thì bên vi phạm phải bồi thường.\n" +
                "- Bên muốn chấm dứt hợp đồng trước thời hạn phải báo trước cho bên kia theo số ngày đã thỏa thuận."),
            DisputeClause
        ],
        [
            new("late_payment_days", "Số ngày chậm trả tối đa", CustomFieldType.Number, true, null, "ngày",
                "Quá số ngày này bên A được chấm dứt hợp đồng"),
            .. UtilityFields
        ],
        NoDeposit: true);

    public static readonly IReadOnlyList<ContractTemplateInput> All = [RoomRental, RoomRentalNoDeposit, WholeHouseRental];
}

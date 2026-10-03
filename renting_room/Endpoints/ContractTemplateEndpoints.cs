using Mediator;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record UpdateContractTemplateRequest(
    string Name,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause>? Clauses,
    IReadOnlyList<CustomFieldDefinition>? Fields,
    uint Version,
    bool NoDeposit = false);

/// <summary>Mẫu hợp đồng của tổ chức (CT-BR-25) — tùy biến tiêu đề, điều khoản, trường theo từng loại hợp đồng.</summary>
public static class ContractTemplateEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/contract-templates";

    public static void MapContractTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithTags("Contract templates").RequireAuthorization(AuthPolicies.OrgMember);

        group.MapGet("/", async (ContractType? type, bool? includeArchived, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListContractTemplatesQuery(type, includeArchived ?? false), ct)))
            .WithSummary("Danh sách mẫu hợp đồng (mảng) — lọc theo loại");

        group.MapGet("/presets", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetContractTemplatePresetsQuery(), ct)))
            .WithSummary("Mẫu gợi ý dựng sẵn (thuê phòng trọ, thuê nhà nguyên căn) — dùng làm điểm xuất phát để tạo mẫu");

        group.MapPost("/", async (ContractTemplateInput body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateContractTemplateCommand(body), ct)).ToCreated(Route))
            .WithIdempotency(required: true)
            .WithSummary("Tạo mẫu hợp đồng");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetContractTemplateQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết mẫu");

        group.MapPut("/{id:guid}", async (Guid id, UpdateContractTemplateRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateContractTemplateCommand(
                    id, new ContractTemplateInput(b.Name, b.ContractType, b.Title, b.Clauses, b.Fields, b.NoDeposit), b.Version), ct)).ToHttp())
            .WithSummary("Sửa mẫu — không ảnh hưởng hợp đồng đã kích hoạt");

        group.MapPost("/{id:guid}/archive", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeContractTemplateStateCommand(id, Archive: true), ct)).ToHttp())
            .WithSummary("Ngừng dùng mẫu (không chọn được cho hợp đồng mới)");

        group.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeContractTemplateStateCommand(id, Archive: false), ct)).ToHttp())
            .WithSummary("Khôi phục mẫu");
    }
}

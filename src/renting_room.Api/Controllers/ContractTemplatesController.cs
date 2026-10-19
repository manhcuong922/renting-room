using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>Mẫu hợp đồng của tổ chức (CT-BR-25) — tùy biến tiêu đề, điều khoản, trường theo từng loại hợp đồng.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Contract templates")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class ContractTemplatesController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/contract-templates";

    [HttpGet("contract-templates")]
    [EndpointSummary("Danh sách mẫu hợp đồng (mảng) — lọc theo loại")]
    public async Task<IResult> List(ContractType? type, bool? includeArchived, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListContractTemplatesQuery(type, includeArchived ?? false), ct));

    [HttpGet("contract-templates/presets")]
    [EndpointSummary("Mẫu gợi ý dựng sẵn (thuê phòng trọ, thuê nhà nguyên căn) — dùng làm điểm xuất phát để tạo mẫu")]
    public async Task<IResult> GetPresets(CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetContractTemplatePresetsQuery(), ct));

    [HttpPost("contract-templates")]
    [Idempotent]
    [EndpointSummary("Tạo mẫu hợp đồng")]
    public async Task<IResult> Create(ContractTemplateInput body, CancellationToken ct) =>
        (await sender.Send(new CreateContractTemplateCommand(body), ct)).ToCreated(Route);

    [HttpGet("contract-templates/{id:guid}")]
    [EndpointSummary("Chi tiết mẫu")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetContractTemplateQuery(id), ct)).ToHttp();

    [HttpPut("contract-templates/{id:guid}")]
    [EndpointSummary("Sửa mẫu — không ảnh hưởng hợp đồng đã kích hoạt")]
    public async Task<IResult> Update(Guid id, UpdateContractTemplateRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateContractTemplateCommand(
            id, new ContractTemplateInput(b.Name, b.ContractType, b.Title, b.Clauses, b.Fields, b.NoDeposit), b.Version), ct)).ToHttp();

    [HttpPost("contract-templates/{id:guid}/archive")]
    [EndpointSummary("Ngừng dùng mẫu (không chọn được cho hợp đồng mới)")]
    public async Task<IResult> Archive(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeContractTemplateStateCommand(id, Archive: true), ct)).ToHttp();

    [HttpPost("contract-templates/{id:guid}/restore")]
    [EndpointSummary("Khôi phục mẫu")]
    public async Task<IResult> Restore(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeContractTemplateStateCommand(id, Archive: false), ct)).ToHttp();
}

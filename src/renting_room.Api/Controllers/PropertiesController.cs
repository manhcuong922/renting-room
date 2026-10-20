using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using renting_room.Application.Common.Models;
using renting_room.Application.Properties;
using renting_room.Domain.Properties;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Properties")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class PropertiesController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/properties";

    [HttpGet("properties")]
    [EndpointSummary("Danh sách khu trọ (kèm số phòng, số phòng đang thuê)")]
    public async Task<IResult> List(string? search, bool? includeArchived, int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await sender.Send(
            new ListPropertiesQuery(search, includeArchived ?? false, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct));

    [HttpPost("properties")]
    [Idempotent]
    [EndpointSummary("Tạo khu trọ")]
    public async Task<IResult> Create(CreatePropertyCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToCreated(Route);

    [HttpGet("properties/{id:guid}")]
    [EndpointSummary("Chi tiết khu trọ (số giấy tờ bên cho thuê đã che)")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPropertyQuery(id), ct)).ToHttp();

    [HttpPut("properties/{id:guid}")]
    [EndpointSummary("Sửa thông tin chung của khu (cài đặt kỳ thu: PUT /{id}/billing)")]
    public async Task<IResult> Update(Guid id, UpdatePropertyRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdatePropertyCommand(
            id, body.Name, body.Address, body.Description, body.EvnCustomerCode, body.Land, body.Version), ct)).ToHttp();

    [HttpGet("properties/{id:guid}/billing/preview")]
    [EndpointSummary("K4: xem trước kỳ chuyển tiếp khi đổi ngày chốt / thu trước–thu sau (dư / thiếu bao nhiêu ngày, gợi ý, tiền từng phòng)")]
    public async Task<IResult> PreviewBillingChange(Guid id, int anchorDay, ChargeMode chargeMode, CancellationToken ct) =>
        (await sender.Send(new PreviewBillingChangeQuery(id, anchorDay, chargeMode), ct)).ToHttp();

    [HttpPut("properties/{id:guid}/billing")]
    [EndpointSummary("Cài đặt kỳ thu của khu — mọi HĐ dùng chung (PR-BR-09); khu đã có phiếu ⇒ có kỳ chuyển tiếp (K4)")]
    public async Task<IResult> UpdateBilling(Guid id, UpdatePropertyBillingRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdatePropertyBillingCommand(id,
            new PropertyBillingInput(b.AnchorDay, b.ChargeMode, b.PaymentDueDays, b.ProrationMode, b.NoticeDays, b.RoundInvoiceTotal),
            b.TransitionAdjustDays), ct)).ToHttp();

    [HttpPost("properties/{id:guid}/archive")]
    [EndpointSummary("Ngừng sử dụng khu (cùng toàn bộ phòng)")]
    public async Task<IResult> Archive(Guid id, CancellationToken ct) =>
        (await sender.Send(new ArchivePropertyCommand(id), ct)).ToHttp();

    [HttpPost("properties/{id:guid}/restore")]
    [EndpointSummary("Khôi phục khu")]
    public async Task<IResult> Restore(Guid id, CancellationToken ct) =>
        (await sender.Send(new RestorePropertyCommand(id), ct)).ToHttp();

    [HttpPut("properties/{id:guid}/lessor")]
    [EndpointSummary("Khai bên cho thuê riêng của khu (khác chủ trọ — PR-BR-17). idNumber = null ⇒ giữ số cũ")]
    public async Task<IResult> UpdateLessor(Guid id, UpdateLessorRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateLessorCommand(
            id, b.Type, b.Name, b.Address, b.Phone, b.Email, b.IdType, b.IdNumber, b.IdIssueDate, b.IdIssuePlace,
            b.DateOfBirth, b.TaxCode, b.RepresentativeName, b.RepresentativeTitle, b.AuthorizationDocNo, b.AuthorizationDocDate), ct)).ToHttp();

    [HttpDelete("properties/{id:guid}/lessor")]
    [EndpointSummary("Bỏ bên cho thuê riêng ⇒ khu dùng thông tin chủ trọ (GET /org/lessor)")]
    public async Task<IResult> ClearLessor(Guid id, CancellationToken ct) =>
        (await sender.Send(new ClearPropertyLessorCommand(id), ct)).ToHttp();

    [HttpPost("properties/{id:guid}/lessor/reveal-id-number")]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [EndpointSummary("Xem số giấy tờ đầy đủ của bên cho thuê hiệu lực của khu (có ghi audit; giới hạn theo user)")]
    public async Task<IResult> RevealLessorIdNumber(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new RevealLessorIdNumberQuery(id), ct);
        return result.IsSuccess ? Results.Ok(new RevealedIdNumber(result.Value!)) : result.ToHttp();
    }

    [HttpPut("properties/{id:guid}/bank-account")]
    [EndpointSummary("Tài khoản nhận tiền (gửi cả 3 trường null để xóa)")]
    public async Task<IResult> UpdateBankAccount(Guid id, UpdateBankAccountRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateBankAccountCommand(id, body.BankName, body.AccountNo, body.AccountName), ct)).ToHttp();

    [HttpPut("properties/{id:guid}/house-rules")]
    [EndpointSummary("Nội quy khu trọ (đính kèm hợp đồng khi kích hoạt)")]
    public async Task<IResult> UpdateHouseRules(Guid id, UpdateHouseRulesRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateHouseRulesCommand(id, body.Text), ct)).ToHttp();
}

using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Http;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Organization members")]
[Consumes("application/json")]
public sealed class OrganizationController(ISender sender) : ControllerBase
{
    [HttpGet("org")]
    [Authorize(Policy = AuthPolicies.OrgMember)]
    [EndpointSummary("Thông tin tổ chức (tên, liên hệ, mã số thuế, địa chỉ)")]
    public async Task<IResult> GetInfo(CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetOrganizationInfoQuery(), ct));

    [HttpPut("org")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Chủ trọ sửa thông tin tổ chức — mã tổ chức không đổi")]
    public async Task<IResult> UpdateInfo(UpdateOrganizationInfoRequest b, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateOrganizationInfoCommand(b.Name, b.ContactName, b.ContactPhone, b.ContactEmail, b.TaxCode, b.Address), ct));

    [HttpGet("org/lessor")]
    [Authorize(Policy = AuthPolicies.OrgMember)]
    [EndpointSummary("Thông tin chủ trọ làm bên cho thuê (lessor = null ⇒ chưa khai; prefill = gợi ý từ thông tin liên hệ)")]
    public async Task<IResult> GetLessor(CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetOrganizationLessorQuery(), ct));

    [HttpPut("org/lessor")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Khai thông tin chủ trọ làm bên cho thuê — chỉ chủ trọ. idNumber = null ⇒ giữ số cũ")]
    public async Task<IResult> UpdateLessor(UpdateLessorRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateOrganizationLessorCommand(
            b.Type, b.Name, b.Address, b.Phone, b.Email, b.IdType, b.IdNumber, b.IdIssueDate, b.IdIssuePlace,
            b.DateOfBirth, b.TaxCode, b.RepresentativeName, b.RepresentativeTitle, b.AuthorizationDocNo, b.AuthorizationDocDate), ct)).ToHttp();

    [HttpPost("org/lessor/reveal-id-number")]
    [Authorize(Policy = AuthPolicies.OrgMember)]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [EndpointSummary("Xem số giấy tờ đầy đủ của chủ trọ (có ghi audit; giới hạn theo user)")]
    public async Task<IResult> RevealLessorIdNumber(CancellationToken ct)
    {
        var result = await sender.Send(new RevealOrganizationLessorIdNumberQuery(), ct);
        return result.IsSuccess ? Results.Ok(new RevealedIdNumber(result.Value!)) : result.ToHttp();
    }

    [HttpGet("org/data-retention")]
    [Authorize(Policy = AuthPolicies.OrgMember)]
    [EndpointSummary("Thời gian giữ dữ liệu cá nhân người thuê (tháng) và trạng thái ẩn danh tự động")]
    public async Task<IResult> GetDataRetention(CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetDataRetentionQuery(), ct));

    [HttpPut("org/data-retention")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Đặt thời gian giữ (36–120 tháng) / tắt ẩn danh tự động — chỉ chủ trọ; tắt ⇒ cảnh báo AUTO_ANONYMIZE_DISABLED")]
    public async Task<IResult> UpdateDataRetention(UpdateDataRetentionRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateDataRetentionCommand(body.RetentionMonths, body.AutoAnonymize), ct));
}

using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using renting_room.Application.Common.Models;
using renting_room.Application.Renters;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>Người thuê / người ở (M03 — phần cơ bản). Số giấy tờ luôn che, xem đầy đủ qua reveal có ghi log.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Renters")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class RentersController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/renters";

    [HttpGet("renters")]
    [EndpointSummary("Danh sách / tìm người thuê theo tên (không dấu) / SĐT; lọc theo khu, đang ở / không còn ở, kèm hồ sơ đã ẩn. Tìm theo số giấy tờ: POST /renters/search")]
    public async Task<IResult> List(
        string? q, Guid? propertyId, bool? staying, bool? includeArchived, int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SearchRentersQuery(q, null, null, page ?? 1, pageSize ?? Paging.DefaultPageSize, propertyId, staying,
            includeArchived ?? false), ct));

    [HttpPost("renters/{id:guid}/archive")]
    [EndpointSummary("Ẩn hồ sơ khỏi danh sách / tìm kiếm mặc định (không xóa; người còn HĐ đang chạy thì không ẩn được)")]
    public async Task<IResult> Archive(Guid id, CancellationToken ct) =>
        (await sender.Send(new ArchiveRenterCommand(id, true), ct)).ToHttp();

    [HttpPost("renters/{id:guid}/restore")]
    [EndpointSummary("Hiện lại hồ sơ đã ẩn")]
    public async Task<IResult> Restore(Guid id, CancellationToken ct) =>
        (await sender.Send(new ArchiveRenterCommand(id, false), ct)).ToHttp();

    [HttpGet("renters/{id:guid}/consents")]
    [EndpointSummary("Các lần ghi nhận đồng ý xử lý dữ liệu của người thuê (mới nhất trước)")]
    public async Task<IResult> ListConsents(Guid id, CancellationToken ct) =>
        (await sender.Send(new ListRenterConsentsQuery(id), ct)).ToHttp();

    [HttpPost("renters/{id:guid}/consents")]
    [EndpointSummary("Ghi nhận đồng ý xử lý dữ liệu: mục đích, phương thức (ký giấy / điều khoản HĐ / miệng), ngày")]
    public async Task<IResult> AddConsent(Guid id, AddConsentRequest b, CancellationToken ct) =>
        (await sender.Send(new AddRenterConsentCommand(id, b.Purpose, b.Method, b.GivenAt, b.Note), ct)).ToHttp();

    [HttpPost("renters/{id:guid}/consents/{consentId:guid}/withdraw")]
    [EndpointSummary("Ghi nhận người thuê rút lại đồng ý (giữ dòng, ghi ngày rút)")]
    public async Task<IResult> WithdrawConsent(Guid id, Guid consentId, WithdrawConsentRequest b, CancellationToken ct) =>
        (await sender.Send(new WithdrawRenterConsentCommand(id, consentId, b.WithdrawnOn), ct)).ToHttp();

    // POST nhưng chỉ đọc: không cần Idempotency-Key.
    [HttpPost("renters/search")]
    [NoStore]
    [EndpointSummary("Tìm người thuê, gồm số giấy tờ khớp chính xác (số giấy tờ gửi trong body, không đặt trên URL)")]
    public async Task<IResult> Search(SearchRentersRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SearchRentersQuery(
            body.Q, body.IdNumber, body.IdType, body.Page ?? 1, body.PageSize ?? Paging.DefaultPageSize), ct));

    [HttpPost("renters")]
    [Idempotent]
    [EndpointSummary("Tạo hồ sơ người thuê (409 nếu số giấy tờ đã có — dùng lại hồ sơ cũ)")]
    public async Task<IResult> Create(RenterInput body, CancellationToken ct) =>
        (await sender.Send(new CreateRenterCommand(body), ct)).ToCreated(Route);

    [HttpGet("renters/{id:guid}")]
    [EndpointSummary("Chi tiết người thuê")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetRenterQuery(id), ct)).ToHttp();

    [HttpPut("renters/{id:guid}")]
    [EndpointSummary("Sửa hồ sơ (idNumber = null ⇒ giữ số giấy tờ cũ)")]
    public async Task<IResult> Update(Guid id, UpdateRenterRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateRenterCommand(id, body.Renter, body.Version), ct)).ToHttp();

    [HttpPost("renters/{id:guid}/reveal-id-number")]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [EndpointSummary("Xem số giấy tờ đầy đủ (có ghi audit; giới hạn theo user)")]
    public async Task<IResult> RevealIdNumber(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new RevealRenterIdNumberQuery(id), ct);
        return result.IsSuccess ? Results.Ok(new RevealedIdNumber(result.Value!)) : result.ToHttp();
    }

    [HttpPost("renters/{id:guid}/anonymize")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Ẩn danh ngay (người thuê yêu cầu xóa dữ liệu / xóa sớm) — chỉ chủ trọ, lý do bắt buộc, KHÔNG đảo ngược")]
    public async Task<IResult> Anonymize(Guid id, AnonymizeRenterRequest body, CancellationToken ct) =>
        (await sender.Send(new AnonymizeRenterCommand(id, body.Reason), ct)).ToHttp();
}

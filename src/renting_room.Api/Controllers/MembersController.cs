using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Members;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Organization members")]
[Consumes("application/json")]
public sealed class MembersController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/org/members";

    [HttpGet("org/members")]
    [Authorize(Policy = AuthPolicies.OrgMember)]
    [EndpointSummary("Danh sách chủ trọ và phó quản lý")]
    public async Task<IResult> List(bool? includeRemoved, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListMembersQuery(includeRemoved ?? false), ct));

    [HttpGet("org/members/{id:guid}")]
    [Authorize(Policy = AuthPolicies.OrgMember)]
    [EndpointSummary("Chi tiết thành viên")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetMemberQuery(id), ct)).ToHttp();

    [HttpGet("org/login-events")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Nhật ký đăng nhập của các tài khoản trong tổ chức (thành công / thất bại, lý do, IP) — chỉ chủ trọ, mới nhất trước")]
    public async Task<IResult> ListLoginEvents(Guid? userId, bool? success, int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListLoginEventsQuery(userId, success, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct));

    [HttpPost("org/members")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [Idempotent]
    [NoStore]
    [EndpointSummary("Thêm phó quản lý — trả mật khẩu tạm một lần (hết hạn sau 72 giờ)")]
    public async Task<IResult> CreateManager(CreateManagerRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new CreateManagerCommand(body.FullName, body.Phone, body.Email), ct);
        return result.IsSuccess
            ? Results.Created($"{Route}/{result.Value!.UserId}", result.Value)
            : result.ToHttp();
    }

    [HttpPut("org/members/{id:guid}")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Sửa thông tin phó quản lý")]
    public async Task<IResult> UpdateManager(Guid id, UpdateManagerRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateManagerCommand(id, body.FullName, body.Phone, body.Email, body.Version), ct)).ToHttp();

    [HttpPost("org/members/{id:guid}/lock")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Khóa phó quản lý (đăng xuất ngay mọi phiên)")]
    public async Task<IResult> Lock(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeManagerStatusCommand(id, ManagerAction.Lock), ct)).ToHttp();

    [HttpPost("org/members/{id:guid}/unlock")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Mở khóa phó quản lý")]
    public async Task<IResult> Unlock(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeManagerStatusCommand(id, ManagerAction.Unlock), ct)).ToHttp();

    [HttpPost("org/members/{id:guid}/remove")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Gỡ phó quản lý khỏi tổ chức (vĩnh viễn)")]
    public async Task<IResult> Remove(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeManagerStatusCommand(id, ManagerAction.Remove), ct)).ToHttp();

    [HttpPut("org/members/{id:guid}/sensitive-data-access")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Cấp / thu hồi quyền xem dữ liệu nhạy cảm (số giấy tờ đầy đủ) cho phó quản lý")]
    public async Task<IResult> SetSensitiveDataAccess(Guid id, SensitiveDataAccessRequest body, CancellationToken ct) =>
        (await sender.Send(new SetSensitiveDataAccessCommand(id, body.Allowed), ct)).ToHttp();

    [HttpPut("org/members/{id:guid}/write-off-permission")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [EndpointSummary("Cấp / thu hồi quyền bỏ nợ cho từng phó quản lý (mặc định không)")]
    public async Task<IResult> SetWriteOffPermission(Guid id, WriteOffPermissionRequest body, CancellationToken ct) =>
        (await sender.Send(new SetWriteOffPermissionCommand(id, body.Allowed), ct)).ToHttp();

    [HttpPost("org/members/{id:guid}/reset-password")]
    [Authorize(Policy = AuthPolicies.OrgOwner)]
    [NoStore]
    [EndpointSummary("Cấp lại mật khẩu tạm cho phó quản lý")]
    public async Task<IResult> ResetPassword(Guid id, CancellationToken ct) =>
        (await sender.Send(new ResetManagerPasswordCommand(id), ct)).ToHttp();
}

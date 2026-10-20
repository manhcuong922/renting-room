using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Identity.Members;
using renting_room.Http;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Admin - Users")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.SystemAdmin)]
public sealed class AdminUsersController(ISender sender) : ControllerBase
{
    [HttpPost("admin/users/{id:guid}/reset-password")]
    [NoStore]
    [EndpointSummary("Cấp lại mật khẩu tạm cho chủ trọ / phó quản lý")]
    public async Task<IResult> ResetPassword(Guid id, CancellationToken ct) =>
        (await sender.Send(new AdminResetPasswordCommand(id), ct)).ToHttp();

    [HttpPost("admin/users/{id:guid}/lock")]
    [EndpointSummary("Khóa tài khoản phó quản lý (chủ trọ: dùng tạm ngưng tổ chức)")]
    public async Task<IResult> Lock(Guid id, CancellationToken ct) =>
        (await sender.Send(new AdminSetUserLockCommand(id, Locked: true), ct)).ToHttp();

    [HttpPost("admin/users/{id:guid}/unlock")]
    [EndpointSummary("Mở khóa tài khoản")]
    public async Task<IResult> Unlock(Guid id, CancellationToken ct) =>
        (await sender.Send(new AdminSetUserLockCommand(id, Locked: false), ct)).ToHttp();
}

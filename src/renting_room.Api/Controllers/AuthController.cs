using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using renting_room.Application.Identity.Auth;
using renting_room.Application.Identity.Auth.ChangePassword;
using renting_room.Application.Identity.Auth.Login;
using renting_room.Application.Identity.Auth.Logout;
using renting_room.Application.Identity.Auth.Refresh;
using renting_room.Application.Identity.Me;
using renting_room.Errors;
using renting_room.Http;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Auth")]
[Consumes("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [EndpointSummary("Đăng nhập bằng số điện thoại hoặc email")]
    public async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> Login(
        LoginRequest request, [FromServices] LoginAttemptLimiter limiter, CancellationToken cancellationToken)
    {
        using var lease = limiter.Acquire(HttpContext, request.Username);
        if (!lease.IsAcquired)
            return RateLimitingSetup.TooManyRequests(HttpContext, lease);

        var result = await sender.Send(
            new LoginCommand(request.Username, request.Password, HttpContext.ClientIp()), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    [HttpPost("auth/refresh")]
    [AllowAnonymous]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [EndpointSummary("Đổi refresh token lấy cặp token mới (refresh token cũ bị thu hồi)")]
    public async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RefreshTokenCommand(request.RefreshToken, HttpContext.ClientIp()), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    [HttpPost("auth/logout")]
    [AllowAnonymous]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [EndpointSummary("Đăng xuất phiên hiện tại (thu hồi refresh token)")]
    public async Task<Results<NoContent, ProblemHttpResult>> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    [HttpPost("auth/logout-all")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [EndpointSummary("Đăng xuất khỏi mọi thiết bị")]
    public async Task<Results<NoContent, ProblemHttpResult>> LogoutAll(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LogoutAllCommand(), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    // Không cần Idempotency-Key: gửi lại sau khi đổi thành công sẽ bị 401 SESSION_REVOKED ngay ở bước xác thực
    // (stamp đã đổi) → không chạm handler, không cộng bộ đếm khóa; client đăng nhập lại bằng mật khẩu mới.
    [HttpPost("auth/change-password")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    [NoStore]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [EndpointSummary("Đổi mật khẩu — thu hồi mọi phiên khác và trả cặp token mới")]
    public async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> ChangePassword(
        ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, HttpContext.ClientIp()),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    [EndpointSummary("Thông tin tài khoản đang đăng nhập")]
    public async Task<Results<Ok<MeDto>, ProblemHttpResult>> GetMe(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMeQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    [HttpPut("me")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    [EndpointSummary("Chủ trọ tự sửa họ tên, SĐT, email — đổi SĐT / email phải gửi currentPassword và đăng nhập lại; phó quản lý: 403 (chủ trọ sửa)")]
    public async Task<IResult> UpdateMe(UpdateMeRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateMeCommand(b.FullName, b.Phone, b.Email, b.CurrentPassword), ct)).ToHttp();
}

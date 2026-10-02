using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using renting_room.Application.Identity.Auth;
using renting_room.Application.Identity.Auth.ChangePassword;
using renting_room.Application.Identity.Auth.Login;
using renting_room.Application.Identity.Auth.Logout;
using renting_room.Application.Identity.Auth.Refresh;
using renting_room.Application.Identity.Me;
using renting_room.Errors;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup($"{EndpointHelpers.ApiPrefix}/auth")
            .WithTags("Auth")
            .WithNoStore();

        auth.MapPost("/login", Login)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithSummary("Đăng nhập bằng số điện thoại hoặc email");

        auth.MapPost("/refresh", Refresh)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Refresh)
            .WithSummary("Đổi refresh token lấy cặp token mới (refresh token cũ bị thu hồi)");

        auth.MapPost("/logout", Logout)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Refresh)
            .WithSummary("Đăng xuất phiên hiện tại (thu hồi refresh token)");

        auth.MapPost("/logout-all", LogoutAll)
            .RequireAuthorization(AuthPolicies.AnyUser)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithSummary("Đăng xuất khỏi mọi thiết bị");

        // Không cần Idempotency-Key: gửi lại sau khi đổi thành công sẽ bị 401 SESSION_REVOKED ngay ở bước xác thực
        // (stamp đã đổi) → không chạm handler, không cộng bộ đếm khóa; client đăng nhập lại bằng mật khẩu mới.
        auth.MapPost("/change-password", ChangePassword)
            .RequireAuthorization(AuthPolicies.AnyUser)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithSummary("Đổi mật khẩu — thu hồi mọi phiên khác và trả cặp token mới");

        app.MapGet($"{EndpointHelpers.ApiPrefix}/me", GetMe)
            .WithTags("Auth")
            .RequireAuthorization(AuthPolicies.AnyUser)
            .WithSummary("Thông tin tài khoản đang đăng nhập");
    }

    private static async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> Login(
        LoginRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new LoginCommand(request.Username, request.Password, httpContext.ClientIp()), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> Refresh(
        RefreshTokenRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RefreshTokenCommand(request.RefreshToken, httpContext.ClientIp()), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Logout(
        RefreshTokenRequest request, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LogoutAll(
        ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LogoutAllCommand(), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> ChangePassword(
        ChangePasswordRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, httpContext.ClientIp()),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<MeDto>, ProblemHttpResult>> GetMe(
        ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMeQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }
}

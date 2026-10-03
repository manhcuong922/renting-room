using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using renting_room.Application.Common.Security;
using renting_room.Domain.Identity;
using renting_room.Errors;

namespace renting_room.Security;

public static class AuthPolicies
{
    /// <summary>Chỉ cần đăng nhập — kể cả khi đang bắt buộc đổi mật khẩu (dùng cho /me, đổi mật khẩu, đăng xuất).</summary>
    public const string AnyUser = "AnyUser";

    /// <summary>Quản trị nền tảng.</summary>
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>Thành viên tổ chức chủ trọ. Đây cũng là policy MẶC ĐỊNH cho mọi endpoint không khai báo gì.</summary>
    public const string OrgMember = "OrgMember";

    /// <summary>Chỉ chủ trọ — quản lý phó quản lý (ID-BR-14).</summary>
    public const string OrgOwner = "OrgOwner";
}

internal static class AuthorizationSetup
{
    public const string PasswordChangeRequiredCode = "PASSWORD_CHANGE_REQUIRED";

    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        var orgMemberPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(nameof(UserRole.OrgOwner), nameof(UserRole.OrgManager))
            .RequireClaim(AppClaimTypes.OrganizationId)
            .AddRequirements(new PasswordChangedRequirement())
            .Build();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.AnyUser, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.SystemAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.SystemAdmin))
                .AddRequirements(new PasswordChangedRequirement()))
            .AddPolicy(AuthPolicies.OrgMember, orgMemberPolicy)
            .AddPolicy(AuthPolicies.OrgOwner, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.OrgOwner))
                .RequireClaim(AppClaimTypes.OrganizationId)
                .AddRequirements(new PasswordChangedRequirement()))
            // Secure-by-default: endpoint quên khai báo quyền vẫn bị bảo vệ (M01 §9).
            .SetFallbackPolicy(orgMemberPolicy);

        services.AddSingleton<IAuthorizationHandler, PasswordChangedHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();
        return services;
    }

    /// <summary>ID-BR-07: user có mật khẩu tạm chỉ được đổi mật khẩu / xem /me / đăng xuất.</summary>
    private sealed class PasswordChangedRequirement : IAuthorizationRequirement;

    private sealed class PasswordChangedHandler : AuthorizationHandler<PasswordChangedRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PasswordChangedRequirement requirement)
        {
            if (context.User.HasClaim(AppClaimTypes.MustChangePassword, "true"))
                context.Fail(new AuthorizationFailureReason(this, PasswordChangeRequiredCode));
            else
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }

    /// <summary>Trả 403 dạng ProblemDetails với mã cụ thể thay vì body rỗng mặc định.</summary>
    private sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(
            RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (!authorizeResult.Forbidden)
            {
                await _default.HandleAsync(next, context, policy, authorizeResult);
                return;
            }

            var mustChangePassword = authorizeResult.AuthorizationFailure?.FailureReasons
                .Any(r => r.Message == PasswordChangeRequiredCode) == true;

            if (mustChangePassword)
            {
                await ProblemResponses.WriteAsync(context, StatusCodes.Status403Forbidden, PasswordChangeRequiredCode,
                    "Bạn cần đổi mật khẩu trước khi tiếp tục.");
                return;
            }

            await ProblemResponses.WriteAsync(context, StatusCodes.Status403Forbidden, "FORBIDDEN",
                "Bạn không có quyền thực hiện thao tác này.");
        }
    }
}

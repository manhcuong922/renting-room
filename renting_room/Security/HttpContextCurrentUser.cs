using System.Security.Claims;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Identity;

namespace renting_room.Security;

/// <summary>Đọc người dùng hiện tại từ claim của access token đã được JwtBearer xác thực.</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ReadGuid(AppClaimTypes.Subject);

    public Guid? OrganizationId => ReadGuid(AppClaimTypes.OrganizationId);

    public UserRole? Role =>
        IsAuthenticated && Enum.TryParse<UserRole>(Principal!.FindFirstValue(AppClaimTypes.Role), out var role)
            ? role
            : null;

    private Guid? ReadGuid(string claimType) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(claimType), out var value) ? value : null;
}

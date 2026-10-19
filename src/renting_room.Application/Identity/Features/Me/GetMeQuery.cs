using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Me;

public sealed record GetMeQuery : IRequest<Result<MeDto>>;

public sealed record MeOrganizationDto(Guid Id, string Code, string Name);

public sealed record MeDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    UserRole Role,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    MeOrganizationDto? Organization,
    bool CanViewSensitiveData,
    bool CanWriteOff = false);

public sealed class GetMeHandler(IUserQueries users, ICurrentUser currentUser)
    : IRequestHandler<GetMeQuery, Result<MeDto>>
{
    public async ValueTask<Result<MeDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var me = await users.GetMeAsync(userId, cancellationToken);

        return me is null ? IdentityErrors.UserNotFound : me;
    }
}

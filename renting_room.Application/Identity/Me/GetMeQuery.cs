using Mediator;
using Microsoft.EntityFrameworkCore;
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
    MeOrganizationDto? Organization);

public sealed class GetMeHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMeQuery, Result<MeDto>>
{
    public async ValueTask<Result<MeDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var me = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new MeDto(
                u.Id,
                u.FullName,
                u.PhoneNormalized,
                u.EmailNormalized,
                u.Role,
                u.MustChangePassword,
                u.LastLoginAt,
                db.Organizations
                    .Where(o => o.Id == u.OrganizationId)
                    .Select(o => new MeOrganizationDto(o.Id, o.Code, o.Name))
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

        return me is null ? IdentityErrors.UserNotFound : me;
    }
}

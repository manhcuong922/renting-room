using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

public sealed record LoginEventDto(
    Guid Id, Guid? UserId, string? FullName, bool Success, LoginOutcome Reason, string? IpAddress, DateTimeOffset OccurredAt);

/// <summary>M01 §11: chủ trọ xem nhật ký đăng nhập của các tài khoản trong tổ chức mình, mới nhất trước.</summary>
public sealed record ListLoginEventsQuery(Guid? UserId, bool? Success, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<LoginEventDto>>;

public sealed class ListLoginEventsQueryValidator : AbstractValidator<ListLoginEventsQuery>
{
    public ListLoginEventsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
    }
}

public sealed class ListLoginEventsHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<ListLoginEventsQuery, PagedResult<LoginEventDto>>
{
    public async ValueTask<PagedResult<LoginEventDto>> Handle(ListLoginEventsQuery request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var query = db.LoginEvents.AsNoTracking().Where(e => e.OrganizationId == organizationId);
        if (request.UserId is { } userId)
            query = query.Where(e => e.UserId == userId);
        if (request.Success is { } success)
            query = query.Where(e => e.Success == success);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(e => e.OccurredAt)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(e => new LoginEventDto(e.Id, e.UserId, db.Users.Where(u => u.Id == e.UserId).Select(u => u.FullName).FirstOrDefault(),
                e.Success, e.Reason, e.IpAddress, e.OccurredAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<LoginEventDto>(items, request.Page, request.PageSize, total);
    }
}

/// <summary>Dọn dữ liệu tài khoản cũ: refresh token hết hạn quá 7 ngày, nhật ký đăng nhập cũ hơn 180 ngày.</summary>
public static class AccountCleanup
{
    public static readonly TimeSpan ExpiredTokenGrace = TimeSpan.FromDays(7);
    public static readonly TimeSpan LoginEventRetention = TimeSpan.FromDays(180);

    public static async Task<(int Tokens, int LoginEvents)> PurgeAsync(IAppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var tokenCutoff = now - ExpiredTokenGrace;
        var eventCutoff = now - LoginEventRetention;
        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < tokenCutoff).ExecuteDeleteAsync(ct);
        var events = await db.LoginEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);
        return (tokens, events);
    }
}

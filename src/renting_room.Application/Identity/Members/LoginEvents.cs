using FluentValidation;
using Mediator;
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

public sealed class ListLoginEventsHandler(IUserQueries users, ICurrentUser currentUser) : IRequestHandler<ListLoginEventsQuery, PagedResult<LoginEventDto>>
{
    public async ValueTask<PagedResult<LoginEventDto>> Handle(ListLoginEventsQuery request, CancellationToken cancellationToken) =>
        await users.ListLoginEventsAsync(currentUser.OrganizationId, request, cancellationToken);
}

/// <summary>Dọn dữ liệu tài khoản cũ: refresh token hết hạn quá 7 ngày, nhật ký đăng nhập cũ hơn 180 ngày.</summary>
public static class AccountCleanup
{
    public static readonly TimeSpan ExpiredTokenGrace = TimeSpan.FromDays(7);
    public static readonly TimeSpan LoginEventRetention = TimeSpan.FromDays(180);

    public static async Task<(int Tokens, int LoginEvents)> PurgeAsync(
        IRefreshTokenRepository refreshTokens, ILoginEventRepository loginEvents, DateTimeOffset now, CancellationToken ct)
    {
        var tokenCutoff = now - ExpiredTokenGrace;
        var eventCutoff = now - LoginEventRetention;
        var tokens = await refreshTokens.DeleteExpiredBeforeAsync(tokenCutoff, ct);
        var events = await loginEvents.DeleteOlderThanAsync(eventCutoff, ct);
        return (tokens, events);
    }
}

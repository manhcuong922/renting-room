using Microsoft.EntityFrameworkCore;
using renting_room.Application.Identity;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class LoginEventRepository(AppDbContext db) : ILoginEventRepository
{
    public void Add(LoginEvent loginEvent) => db.LoginEvents.Add(loginEvent);

    public async Task<LoginFailureStats> GetFailureStatsAsync(
        Guid userId, string? ipAddress, DateTimeOffset now, DateTimeOffset? notBefore, CancellationToken cancellationToken)
    {
        var floor = notBefore ?? DateTimeOffset.MinValue;
        var events = db.LoginEvents.AsNoTracking().Where(e => e.UserId == userId);
        // So IP bằng == của C#: EF sinh "= @ip OR (cả hai NULL)" ⇒ request không có IP vẫn là một "IP" riêng.
        var lastSuccessFromIp = await events.Where(e => e.Success && e.IpAddress == ipAddress)
            .MaxAsync(e => (DateTimeOffset?)e.OccurredAt, cancellationToken);

        var ipSince = Latest(now - LoginLockoutPolicy.IpWindow, lastSuccessFromIp, floor);
        var fromIp = await events.CountAsync(
            e => e.Reason == LoginOutcome.WrongPassword && e.IpAddress == ipAddress && e.OccurredAt > ipSince, cancellationToken);

        var knownSince = now - LoginLockoutPolicy.KnownIpLookback;
        if (lastSuccessFromIp >= knownSince)
            return new LoginFailureStats(fromIp, IsKnownIp: true, FailuresFromUnknownIps: 0);

        var unknownSince = Latest(now - LoginLockoutPolicy.UnknownIpWindow, null, floor);
        var fromUnknown = await events.CountAsync(e => e.Reason == LoginOutcome.WrongPassword && e.OccurredAt > unknownSince
            && !db.LoginEvents.Any(k => k.UserId == userId && k.Success && k.OccurredAt >= knownSince && k.IpAddress == e.IpAddress),
            cancellationToken);
        return new LoginFailureStats(fromIp, IsKnownIp: false, fromUnknown);
    }

    private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset? b, DateTimeOffset c)
    {
        var latest = b > a ? b.Value : a;
        return c > latest ? c : latest;
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.LoginEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}

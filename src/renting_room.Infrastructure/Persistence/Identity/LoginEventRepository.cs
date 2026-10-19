using Microsoft.EntityFrameworkCore;
using renting_room.Application.Identity;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class LoginEventRepository(AppDbContext db) : ILoginEventRepository
{
    public void Add(LoginEvent loginEvent) => db.LoginEvents.Add(loginEvent);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.LoginEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}

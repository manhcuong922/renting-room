using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

public interface ILoginEventRepository
{
    void Add(LoginEvent loginEvent);

    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

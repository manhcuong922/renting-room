using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using renting_room.Domain.Entities;
using renting_room.Domain.Identity;

namespace renting_room.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Room> Rooms { get; }

    /// <summary>Dùng cho transaction tường minh khi một use case cần nhiều lệnh ghi nguyên tử.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

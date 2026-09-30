using Microsoft.EntityFrameworkCore;
using renting_room.Domain.Entities;

namespace renting_room.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Room> Rooms { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

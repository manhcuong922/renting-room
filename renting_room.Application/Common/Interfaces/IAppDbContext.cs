using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Property> Properties { get; }
    DbSet<Room> Rooms { get; }
    DbSet<RoomGroup> RoomGroups { get; }
    DbSet<Renter> Renters { get; }
    DbSet<ContractTemplate> ContractTemplates { get; }
    DbSet<Contract> Contracts { get; }

    /// <summary>Dùng cho transaction tường minh khi một use case cần nhiều lệnh ghi nguyên tử.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimistic concurrency (C-07): đặt version client gửi lên làm giá trị gốc ⇒ SaveChanges ném
    /// DbUpdateConcurrencyException (409) nếu bản ghi đã bị người khác sửa.
    /// </summary>
    void SetExpectedVersion(AuditableEntity entity, uint version);

    /// <summary><c>SELECT … FOR UPDATE</c> trên 1 hàng — tuần tự hóa các lệnh cùng tác động 1 bản ghi. Phải gọi trong transaction.</summary>
    Task LockForUpdateAsync<TEntity>(Guid id, CancellationToken cancellationToken) where TEntity : Entity;

    /// <summary>
    /// Khóa logic theo người thuê tới hết transaction (advisory lock) — tuần tự hóa các lệnh làm thay đổi "người này đang ở đâu"
    /// trên các hợp đồng KHÁC nhau (CT-BR-31), nơi khóa hàng hợp đồng không đủ. Khóa theo thứ tự id tăng dần để tránh deadlock.
    /// </summary>
    Task LockRentersAsync(IEnumerable<Guid> renterIds, CancellationToken cancellationToken);
}

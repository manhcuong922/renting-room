using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using Npgsql;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;
using renting_room.Infrastructure.Idempotency;

namespace renting_room.Infrastructure.Persistence;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    TimeProvider clock)
    : DbContext(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomGroup> RoomGroups => Set<RoomGroup>();
    public DbSet<Renter> Renters => Set<Renter>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<Contract> Contracts => Set<Contract>();

    /// <summary>Bảng kỹ thuật — không đưa vào IAppDbContext để tầng Application không thao tác trực tiếp.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>
    /// Đọc lại ở mỗi truy vấn (không cache lúc khởi tạo) — EF tham số hóa thuộc tính này trong global query filter.
    /// Null (chưa đăng nhập / SystemAdmin) ⇒ không thấy dòng nào của bảng nghiệp vụ (fail-closed).
    /// </summary>
    public Guid? CurrentOrganizationId => currentUser.OrganizationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // btree_gist: cần cho EXCLUDE constraint (hợp đồng / người ở không chồng thời gian — CT-BR-01, CT-BR-08).
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTenantQueryFilters(modelBuilder);
        UseApplicationGeneratedKeys(modelBuilder);
    }

    /// <summary>
    /// Id do domain sinh (Guid.NewGuid) chứ không phải DB. Bắt buộc khai báo ValueGeneratedNever: nếu không, entity con mới
    /// thêm vào collection của aggregate đang được theo dõi (người ở, xe, tài sản…) có sẵn khóa ⇒ EF tưởng là bản ghi cũ,
    /// chạy UPDATE thay vì INSERT và báo lỗi concurrency.
    /// </summary>
    private static void UseApplicationGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
            modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
    }

    public void SetExpectedVersion(AuditableEntity entity, uint version) =>
        Entry(entity).Property(nameof(AuditableEntity.Version)).OriginalValue = version;

    /// <summary>Tên bảng lấy từ model EF (không phải input người dùng) — an toàn khi ghép vào SQL; id truyền bằng tham số.</summary>
    public async Task LockForUpdateAsync<TEntity>(Guid id, CancellationToken cancellationToken) where TEntity : Entity
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks must be taken inside a transaction.");

        var entityType = Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of the model.");
        var table = entityType.GetTableName();
        if (table is null || !table.All(c => c is >= 'a' and <= 'z' or '_'))
            throw new InvalidOperationException($"Unexpected table name '{table}'.");

        var sql = "SELECT 1 FROM \"" + table + "\" WHERE id = @id FOR UPDATE";
        await Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("id", id)], cancellationToken);
    }

    public async Task LockRentersAsync(IEnumerable<Guid> renterIds, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Advisory locks must be taken inside a transaction.");

        // Khóa 64-bit từ 8 byte đầu của id; trùng khóa giữa 2 người chỉ làm tuần tự hóa thừa, không sai dữ liệu.
        foreach (var id in renterIds.Distinct().Order())
        {
            var key = BitConverter.ToInt64(id.ToByteArray(), 0);
            await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(@key)", [new NpgsqlParameter("key", key)], cancellationToken);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantAndAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Mọi entity <see cref="ITenantEntity"/>: <c>e =&gt; e.OrganizationId == CurrentOrganizationId</c>.</summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var tenantTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in tenantTypes)
        {
            var entity = Expression.Parameter(clrType, "e");
            var entityOrganizationId = Expression.Convert(
                Expression.Property(entity, nameof(ITenantEntity.OrganizationId)), typeof(Guid?));
            var currentOrganizationId = Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId));
            var filter = Expression.Lambda(Expression.Equal(entityOrganizationId, currentOrganizationId), entity);

            modelBuilder.Entity(clrType).HasQueryFilter(filter);
        }
    }

    /// <summary>
    /// C-01: gán tổ chức cho entity mới, chặn ghi vào dữ liệu của tổ chức khác; C-10: gán thông tin tạo/sửa.
    /// </summary>
    private void ApplyTenantAndAuditRules()
    {
        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;
        var organizationId = currentUser.OrganizationId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantEntity tenantEntity)
                EnforceTenant(entry.State, tenantEntity, organizationId);

            if (entry.Entity is not AuditableEntity auditable)
                continue;

            if (entry.State == EntityState.Added)
                auditable.MarkCreated(now, userId);
            else if (entry.State == EntityState.Modified)
                auditable.MarkUpdated(now, userId);
        }
    }

    private static void EnforceTenant(EntityState state, ITenantEntity entity, Guid? organizationId)
    {
        switch (state)
        {
            case EntityState.Added when entity.OrganizationId == Guid.Empty:
                entity.AssignOrganization(organizationId
                    ?? throw new InvalidOperationException(
                        $"Cannot create {entity.GetType().Name} without an organization context."));
                break;

            case EntityState.Added or EntityState.Modified or EntityState.Deleted
                when entity.OrganizationId != organizationId:
                throw new InvalidOperationException(
                    $"Cross-organization write to {entity.GetType().Name} was blocked.");
        }
    }
}

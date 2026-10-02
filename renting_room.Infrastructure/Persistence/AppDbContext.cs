using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Entities;
using renting_room.Domain.Identity;
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
    public DbSet<Room> Rooms => Set<Room>();

    /// <summary>Bảng kỹ thuật — không đưa vào IAppDbContext để tầng Application không thao tác trực tiếp.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>
    /// Đọc lại ở mỗi truy vấn (không cache lúc khởi tạo) — EF tham số hóa thuộc tính này trong global query filter.
    /// Null (chưa đăng nhập / SystemAdmin) ⇒ không thấy dòng nào của bảng nghiệp vụ (fail-closed).
    /// </summary>
    public Guid? CurrentOrganizationId => currentUser.OrganizationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTenantQueryFilters(modelBuilder);
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

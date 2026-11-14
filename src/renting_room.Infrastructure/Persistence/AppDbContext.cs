using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using renting_room.Application.Common.Interfaces;
using Npgsql;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Identity;
using renting_room.Domain.Meters;
using renting_room.Domain.Payments;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;
using renting_room.Infrastructure.Auditing;
using renting_room.Infrastructure.Idempotency;

namespace renting_room.Infrastructure.Persistence;

public partial class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    TimeProvider clock,
    DomainEventPublisher? domainEvents = null)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginEvent> LoginEvents => Set<LoginEvent>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomGroup> RoomGroups => Set<RoomGroup>();
    public DbSet<Renter> Renters => Set<Renter>();
    public DbSet<RenterClaim> RenterClaims => Set<RenterClaim>();
    public DbSet<UserPropertyAccess> UserPropertyAccesses => Set<UserPropertyAccess>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<FeeType> FeeTypes => Set<FeeType>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<RoomCharge> RoomCharges => Set<RoomCharge>();
    public DbSet<ContractBillingGap> ContractBillingGaps => Set<ContractBillingGap>();
    public DbSet<InvoiceAdjustment> InvoiceAdjustments => Set<InvoiceAdjustment>();
    public DbSet<RenterConsent> RenterConsents => Set<RenterConsent>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CollectionReduction> CollectionReductions => Set<CollectionReduction>();
    public DbSet<Contract> Contracts => Set<Contract>();

    /// <summary>Bảng kỹ thuật — chỉ Infrastructure dùng, không có repository cho tầng Application.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>C-10 — Application ghi qua <see cref="IAuditTrail"/>; thay đổi entity được ghi tự động khi SaveChanges.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Đọc lại ở mỗi truy vấn (không cache lúc khởi tạo) — EF tham số hóa thuộc tính này trong global query filter.
    /// Null (chưa đăng nhập / SystemAdmin) ⇒ không thấy dòng nào của bảng nghiệp vụ (fail-closed).
    /// </summary>
    public Guid? CurrentOrganizationId => currentUser.OrganizationId;

    /// <summary>M01 ID-BR-24: khu được giao của quản lý khu; null = không giới hạn (chủ trọ, job hệ thống). Đọc lại ở mỗi truy vấn như trên.</summary>
    public Guid[]? CurrentPropertyIds => currentUser.PropertyIds;

    public Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.CurrentTransaction is { } current ? new JoinedTransaction(current) : await Database.BeginTransactionAsync(cancellationToken);

    /// <summary>Lệnh con chạy trong transaction có sẵn: commit / rollback / dispose để transaction ngoài quyết định.</summary>
    private sealed class JoinedTransaction(IDbContextTransaction outer) : IDbContextTransaction
    {
        public Guid TransactionId => outer.TransactionId;
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void SetExpectedVersion(AuditableEntity entity, uint version) =>
        Entry(entity).Property(nameof(AuditableEntity.Version)).OriginalValue = version;

    public Task LockForUpdateAsync<TEntity>(Guid id, CancellationToken cancellationToken) where TEntity : Entity =>
        LockRowAsync<TEntity>(id, "FOR UPDATE", cancellationToken);

    public Task LockForShareAsync<TEntity>(Guid id, CancellationToken cancellationToken) where TEntity : Entity =>
        LockRowAsync<TEntity>(id, "FOR SHARE", cancellationToken);

    /// <summary>
    /// Tên bảng lấy từ model EF (không phải input người dùng) — an toàn khi ghép vào SQL; id truyền bằng tham số.
    /// Bảng của tổ chức lọc thêm <c>organization_id</c> như global query filter (L-18): biết UUID của tổ chức khác cũng không khóa được hàng của họ;
    /// chưa có tổ chức ⇒ <c>= NULL</c> không khớp hàng nào (fail-closed).
    /// </summary>
    private async Task LockRowAsync<TEntity>(Guid id, string mode, CancellationToken cancellationToken) where TEntity : Entity
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks must be taken inside a transaction.");

        var entityType = Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of the model.");
        var table = entityType.GetTableName();
        if (table is null || !table.All(c => c is >= 'a' and <= 'z' or '_'))
            throw new InvalidOperationException($"Unexpected table name '{table}'.");

        var sql = "SELECT 1 FROM \"" + table + "\" WHERE id = @id";
        List<NpgsqlParameter> parameters = [new("id", id)];
        if (typeof(ITenantEntity).IsAssignableFrom(typeof(TEntity)))
        {
            sql += " AND organization_id = @organization_id";
            parameters.Add(new NpgsqlParameter("organization_id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)CurrentOrganizationId ?? DBNull.Value });
        }
        sql += " " + mode;
        await Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
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
        var auditLogs = PrepareForSave();
        var pending = DomainEventPublisher.Collect(ChangeTracker);
        try
        {
            var saved = base.SaveChanges(acceptAllChangesOnSuccess);
            domainEvents?.PublishAsync(pending, CancellationToken.None).GetAwaiter().GetResult();
            return saved;
        }
        catch
        {
            DiscardAuditLogs(auditLogs);
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var auditLogs = PrepareForSave();
        var pending = DomainEventPublisher.Collect(ChangeTracker);
        try
        {
            var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (domainEvents is not null)
                await domainEvents.PublishAsync(pending, cancellationToken);
            return saved;
        }
        catch
        {
            DiscardAuditLogs(auditLogs);
            throw;
        }
    }

    /// <summary>
    /// C-01 + C-10: gán tổ chức / người tạo-sửa, rồi sinh dòng audit cho mọi thay đổi và thêm vào CHÍNH lần lưu này
    /// (cùng batch lệnh, cùng transaction ⇒ không thêm round-trip; rollback thì audit cũng không có).
    /// </summary>
    private List<AuditLog> PrepareForSave()
    {
        var actor = AuditActor.From(currentUser, clock);
        TouchInvoicesWithChangedChildren();
        ApplyTenantAndAuditRules(actor);
        var auditLogs = EntityChangeAudit.Collect(ChangeTracker, actor);
        AuditLogs.AddRange(auditLogs);
        return auditLogs;
    }

    /// <summary>
    /// M07 §10 (05/11/2026): version phiếu (xmin hàng <c>invoices</c>) phải đổi ở mọi thay đổi dòng / đoạn sản lượng — kể cả chỉ sửa ghi chú dòng,
    /// không đổi cột nào của phiếu — để người đang mở phiếu cũ bị 409 khi sửa / chốt. Đánh dấu <c>updated_at</c> ⇒ có câu UPDATE ⇒ xmin mới.
    /// </summary>
    private void TouchInvoicesWithChangedChildren()
    {
        var invoiceIds = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity switch { InvoiceLine line => line.InvoiceId, InvoiceMeterSegment segment => segment.InvoiceId, _ => (Guid?)null })
            .OfType<Guid>()
            .ToHashSet();
        if (invoiceIds.Count == 0)
            return;
        foreach (var entry in ChangeTracker.Entries<Invoice>().Where(e => e.State == EntityState.Unchanged && invoiceIds.Contains(e.Entity.Id)))
            entry.Property(nameof(AuditableEntity.UpdatedAt)).IsModified = true;
    }

    /// <summary>Lưu lỗi (xung đột, vi phạm ràng buộc) ⇒ gỡ dòng audit vừa sinh để lần lưu sau trên cùng context không bị nhân đôi.</summary>
    private void DiscardAuditLogs(List<AuditLog> auditLogs)
    {
        foreach (var auditLog in auditLogs)
            Entry(auditLog).State = EntityState.Detached;
    }

    /// <summary>
    /// Mọi entity <see cref="ITenantEntity"/>: <c>e =&gt; e.OrganizationId == CurrentOrganizationId</c>, cộng phạm vi khu của quản lý khu
    /// (<see cref="PropertyScopeFilter"/>, ID-BR-24) — ghép thành một filter.
    /// </summary>
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
            Expression body = Expression.Equal(entityOrganizationId, currentOrganizationId);
            if (PropertyScopeFilter(clrType) is { } propertyFilter)
                body = Expression.AndAlso(body, ParameterRebinder.Replace(propertyFilter.Body, propertyFilter.Parameters[0], entity));

            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(body, entity));
        }
    }

    private void ApplyTenantAndAuditRules(AuditActor actor)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantEntity tenantEntity)
                EnforceTenant(entry.State, tenantEntity, actor.OrganizationId);
            EnforcePropertyScope(entry);

            if (entry.Entity is not AuditableEntity auditable)
                continue;

            if (entry.State == EntityState.Added)
                auditable.MarkCreated(actor.OccurredAt, actor.UserId);
            else if (entry.State == EntityState.Modified)
                auditable.MarkUpdated(actor.OccurredAt, actor.UserId);
        }
    }

    /// <summary>ID-BR-24: quản lý khu không ghi được bản ghi của khu không được giao (lớp chặn cuối — handler thường đã trả 404 vì không đọc được).</summary>
    private void EnforcePropertyScope(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (CurrentPropertyIds is not { } allowed || entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return;
        var propertyId = entry.Entity switch
        {
            IPropertyScoped scoped => scoped.PropertyId,
            Property property => property.Id,
            _ => (Guid?)null
        };
        if (propertyId is { } id && !allowed.Contains(id))
            throw new InvalidOperationException($"Write to {entry.Entity.GetType().Name} outside the user's assigned properties was blocked.");
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

using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using renting_room.Application.Attachments;
using renting_room.Domain.Attachments;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class AttachmentRepository(AppDbContext db) : IAttachmentRepository
{
    public void Add(Attachment attachment) => db.Attachments.Add(attachment);

    public Task<Attachment?> GetActiveForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == id && a.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ListActiveByOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.DeletedAt == null)
            .OrderBy(a => a.UploadedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ListActiveForUpdateAsync(
        AttachmentOwnerType ownerType, Guid ownerId, AttachmentCategory? category, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.DeletedAt == null && (category == null || a.Category == category))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ListUnpurgedSensitiveForUpdateAsync(
        AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.IsSensitive && a.PurgedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ListPurgeableForUpdateAsync(DateTimeOffset deletedBefore, int take, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.DeletedAt != null && a.DeletedAt < deletedBefore && a.PurgedAt == null)
            .OrderBy(a => a.DeletedAt).ThenBy(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
}

internal sealed class StorageQuota(AppDbContext db) : IStorageQuota
{
    public async Task<bool> TryReserveAsync(long bytes, long quotaBytes, CancellationToken cancellationToken)
    {
        // Một câu lệnh nguyên tử: upload song song không cùng lọt qua hạn mức (§10). Trả 0 dòng = vượt hạn mức.
        var affected = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO organization_storage_usage (organization_id, used_bytes)
            SELECT @org, @bytes WHERE @bytes <= @quota
            ON CONFLICT (organization_id) DO UPDATE
               SET used_bytes = organization_storage_usage.used_bytes + @bytes
             WHERE organization_storage_usage.used_bytes + @bytes <= @quota
            """,
            [Org(), new NpgsqlParameter("bytes", NpgsqlDbType.Bigint) { Value = bytes },
             new NpgsqlParameter("quota", NpgsqlDbType.Bigint) { Value = quotaBytes }],
            cancellationToken);
        return affected > 0;
    }

    public async Task ReleaseAsync(long bytes, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE organization_storage_usage SET used_bytes = GREATEST(0, used_bytes - @bytes) WHERE organization_id = @org",
            [Org(), new NpgsqlParameter("bytes", NpgsqlDbType.Bigint) { Value = bytes }],
            cancellationToken);

    private NpgsqlParameter Org() =>
        new("org", NpgsqlDbType.Uuid) { Value = db.CurrentOrganizationId ?? throw new InvalidOperationException("No organization context.") };
}

/// <summary>Kiểm owner qua global query filter của DbContext ⇒ đúng tổ chức và (quản lý khu) đúng khu được giao.</summary>
internal sealed class AttachmentOwners(AppDbContext db) : IAttachmentOwners
{
    public Task<bool> ExistsAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken) => ownerType switch
    {
        AttachmentOwnerType.Property => db.Properties.AnyAsync(p => p.Id == ownerId, cancellationToken),
        AttachmentOwnerType.Room => db.Rooms.AnyAsync(r => r.Id == ownerId, cancellationToken),
        AttachmentOwnerType.Renter => db.Renters.AnyAsync(r => r.Id == ownerId, cancellationToken),
        AttachmentOwnerType.MeterReading => db.Set<Domain.Meters.MeterReading>().AnyAsync(r => r.Id == ownerId, cancellationToken),
        AttachmentOwnerType.Payment => db.Payments.AnyAsync(p => p.Id == ownerId, cancellationToken),
        _ => Task.FromResult(false) // Contract: ⏸ chưa mở upload
    };
}

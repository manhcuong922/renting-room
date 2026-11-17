using renting_room.Domain.Attachments;

namespace renting_room.Application.Attachments;

/// <summary>FS-BR-09: ẩn danh người thuê ⇒ xóa NGAY file nhạy cảm của họ (không chờ 30 ngày). Metadata còn lại, đánh dấu đã xóa vật lý.</summary>
public sealed class SensitiveFilePurger(
    IAttachmentRepository attachments, IStorageQuota quota, IFileStorageSelector storages, TimeProvider clock)
{
    /// <summary>Đánh dấu xóa + trả lại dung lượng trong transaction hiện tại; gọi <see cref="DeleteFilesAsync"/> sau khi commit.</summary>
    public async Task<IReadOnlyList<(StorageProviderKind Provider, string Key)>> DetachAsync(
        AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        var files = await attachments.ListUnpurgedSensitiveForUpdateAsync(ownerType, ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var file in files)
        {
            file.SoftDelete(now, null);
            file.MarkPurged(now);
        }

        if (files.Count > 0)
            await quota.ReleaseAsync(files.Sum(f => f.SizeBytes), cancellationToken);
        return files.Select(f => (f.StorageProvider, f.StorageKey)).ToList();
    }

    /// <summary>Xóa vật lý sau commit. Kho lỗi ⇒ bỏ qua (metadata đã đánh dấu; job đối soát P2 sẽ dọn) — không làm hỏng việc ẩn danh.</summary>
    public async Task DeleteFilesAsync(IEnumerable<(StorageProviderKind Provider, string Key)> files, CancellationToken cancellationToken)
    {
        foreach (var (provider, key) in files)
        {
            try
            {
                await storages.For(provider).DeleteAsync(key, cancellationToken);
            }
            catch (Exception ex) when (ex is StorageUnavailableException or IOException)
            {
                // Best effort.
            }
        }
    }
}

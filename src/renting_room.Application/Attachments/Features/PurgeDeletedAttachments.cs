using Mediator;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Application.Attachments;

public static class AttachmentPurge
{
    public const int BatchSize = 200;
}

/// <summary>FS-BR-08 / FS-11: dọn file vật lý của tệp xóa mềm quá hạn trong tổ chức hiện tại. Trả số tệp đã dọn.</summary>
public sealed record PurgeDeletedAttachmentsCommand(DateTimeOffset Now) : IRequest<int>;

/// <summary>
/// Xóa file ở kho TRƯỚC, rồi mới đánh dấu <c>purged_at</c> và trả hạn mức: kho lỗi ⇒ bỏ qua tệp đó (không mất dung lượng đã tính, ngày sau thử lại);
/// chết giữa chừng sau khi xóa file ⇒ lần sau xóa lại (xóa tệp không tồn tại coi như xong).
/// </summary>
public sealed class PurgeDeletedAttachmentsHandler(
    IAttachmentRepository attachments, IStorageQuota quota, IFileStorageSelector storages, IStorageSettings settings, IUnitOfWork unitOfWork,
    ILogger<PurgeDeletedAttachmentsHandler> logger)
    : IRequestHandler<PurgeDeletedAttachmentsCommand, int>
{
    public async ValueTask<int> Handle(PurgeDeletedAttachmentsCommand request, CancellationToken cancellationToken)
    {
        var cutoff = request.Now - settings.PurgeAfter;
        var total = 0;
        while (true)
        {
            var batch = await attachments.ListPurgeableForUpdateAsync(cutoff, AttachmentPurge.BatchSize, cancellationToken);
            var releasedBytes = 0L;
            var purged = 0;
            foreach (var file in batch)
            {
                try
                {
                    await storages.For(file.StorageProvider).DeleteAsync(file.StorageKey, cancellationToken);
                }
                catch (Exception ex) when (ex is StorageUnavailableException or IOException)
                {
                    logger.LogWarning(ex, "Attachment {AttachmentId}: storage unavailable, physical delete postponed", file.Id);
                    continue;
                }
                file.MarkPurged(request.Now);
                releasedBytes += file.SizeBytes;
                purged++;
            }

            if (purged > 0)
            {
                await quota.ReleaseAsync(releasedBytes, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            unitOfWork.ClearTracking();
            total += purged;

            // Còn tệp chưa dọn được (kho lỗi) ⇒ dừng, tránh quay vòng đúng những tệp đó.
            if (batch.Count < AttachmentPurge.BatchSize || purged < batch.Count)
                return total;
        }
    }
}

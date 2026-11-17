using System.Security.Cryptography;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Attachments;
using renting_room.Domain.Common;

namespace renting_room.Application.Attachments;

/// <summary>FS-BR-01…05, 10…12: nhận 1–20 tệp cho một đối tượng. Kiểm hết trước, rồi ghi kho + DB trong một transaction.</summary>
public sealed record UploadAttachmentsCommand(
    AttachmentOwnerType OwnerType, Guid OwnerId, AttachmentCategory Category, string? Description, IReadOnlyList<UploadedFile> Files)
    : IRequest<Result<AttachmentListDto>>;

public sealed class UploadAttachmentsHandler(
    IAttachmentRepository attachments,
    IAttachmentOwners owners,
    IStorageQuota quota,
    IFileStorageSelector storages,
    IStorageSettings settings,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock)
    : IRequestHandler<UploadAttachmentsCommand, Result<AttachmentListDto>>
{
    public async ValueTask<Result<AttachmentListDto>> Handle(UploadAttachmentsCommand request, CancellationToken cancellationToken)
    {
        if (request.Files.Count == 0)
            return Result.Failure<AttachmentListDto>(AttachmentErrors.NoFile);
        if (request.Files.Count > AttachmentRules.MaxFilesPerRequest)
            return Result.Failure<AttachmentListDto>(AttachmentErrors.TooManyFiles);
        if (!AttachmentRules.IsAllowed(request.OwnerType, request.Category))
            return Result.Failure<AttachmentListDto>(AttachmentErrors.CategoryNotAllowed);
        if (!await owners.ExistsAsync(request.OwnerType, request.OwnerId, cancellationToken))
            return Result.Failure<AttachmentListDto>(AttachmentErrors.OwnerNotFound);

        var types = new List<DetectedFileType>(request.Files.Count);
        foreach (var file in request.Files)
        {
            if (file.Length is <= 0 or > AttachmentRules.MaxFileBytes)
                return Result.Failure<AttachmentListDto>(file.Length <= 0 ? AttachmentErrors.FileTypeNotAllowed : AttachmentErrors.FileTooLarge);
            var type = await DetectAsync(file, cancellationToken);
            if (type is null)
                return Result.Failure<AttachmentListDto>(AttachmentErrors.FileTypeNotAllowed);
            types.Add(type);
        }

        IFileStorage storage;
        try
        {
            storage = storages.ForNewFiles;
        }
        catch (StorageUnavailableException)
        {
            return Result.Failure<AttachmentListDto>(AttachmentErrors.StorageUnavailable);
        }

        var organizationId = currentUser.OrganizationId!.Value;
        var now = clock.GetUtcNow();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var replaced = AttachmentRules.ReplacesPrevious(request.Category)
            ? await attachments.ListActiveForUpdateAsync(request.OwnerType, request.OwnerId, request.Category, cancellationToken)
            : [];
        if (AttachmentRules.ReplacesPrevious(request.Category) && request.Files.Count > 1)
            return Result.Failure<AttachmentListDto>(AttachmentErrors.TooManyAttachments);
        if (AttachmentRules.MaxPerOwnerCategory(request.Category) is { } max)
        {
            var existing = (await attachments.ListActiveByOwnerAsync(request.OwnerType, request.OwnerId, cancellationToken))
                .Count(a => a.Category == request.Category);
            if (existing + request.Files.Count > max)
                return Result.Failure<AttachmentListDto>(AttachmentErrors.TooManyAttachments);
        }

        if (!await quota.TryReserveAsync(request.Files.Sum(f => f.Length), settings.OrganizationQuotaBytes, cancellationToken))
            return Result.Failure<AttachmentListDto>(AttachmentErrors.QuotaExceeded);

        var written = new List<string>();
        var created = new List<Attachment>(request.Files.Count);
        try
        {
            for (var i = 0; i < request.Files.Count; i++)
            {
                var file = request.Files[i];
                var id = Guid.CreateVersion7();
                var key = AttachmentRules.StorageKey(organizationId, now, id);
                var prepared = await PrepareAsync(file, types[i], cancellationToken);
                if (prepared is null)
                    return await AbortAsync(storage, written, transaction, AttachmentErrors.FileTooLarge, cancellationToken);

                await storage.SaveAsync(key, new MemoryStream(prepared), types[i].ContentType, cancellationToken);
                written.Add(key);
                var attachment = Attachment.Create(id, organizationId, request.OwnerType, request.OwnerId, request.Category, file.FileName,
                    types[i].ContentType, prepared.Length, storage.Kind, key, Convert.ToHexStringLower(SHA256.HashData(prepared)),
                    request.Description, now, currentUser.UserId);
                attachments.Add(attachment);
                created.Add(attachment);
            }

            // Đã giữ chỗ theo kích thước khai báo; ảnh JPEG bị bỏ EXIF nhỏ hơn ⇒ trả lại phần chênh.
            var declared = request.Files.Sum(f => f.Length);
            var actual = created.Sum(a => a.SizeBytes);
            if (actual < declared)
                await quota.ReleaseAsync(declared - actual, cancellationToken);

            foreach (var old in replaced)
                old.SoftDelete(now, currentUser.UserId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (StorageUnavailableException)
        {
            await DeleteQuietlyAsync(storage, written);
            return Result.Failure<AttachmentListDto>(AttachmentErrors.StorageUnavailable);
        }
        catch
        {
            // DB lỗi sau khi đã ghi file ⇒ dọn file, không để file mồ côi (§10).
            await DeleteQuietlyAsync(storage, written);
            throw;
        }

        return new AttachmentListDto(created.Select(AttachmentDto.From).ToList());
    }

    private static async Task<DetectedFileType?> DetectAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.Open();
        var header = new byte[FileSignature.HeaderLength];
        var read = 0;
        while (read < header.Length)
        {
            var n = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
            if (n == 0)
                break;
            read += n;
        }

        return FileSignature.Detect(header.AsSpan(0, read));
    }

    /// <summary>Đọc tối đa 10 MB + 1 byte (không tin <c>Length</c> khai báo); JPEG được bỏ EXIF. Quá cỡ ⇒ null.</summary>
    private static async Task<byte[]?> PrepareAsync(UploadedFile file, DetectedFileType type, CancellationToken cancellationToken)
    {
        await using var stream = file.Open();
        using var buffer = new MemoryStream((int)Math.Min(file.Length, AttachmentRules.MaxFileBytes));
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > AttachmentRules.MaxFileBytes)
                return null;
        }

        var bytes = buffer.ToArray();
        return type.IsJpeg ? JpegMetadata.StripExif(bytes) : bytes;
    }

    private static async Task<Result<AttachmentListDto>> AbortAsync(
        IFileStorage storage, List<string> written, ITransaction transaction, Error error, CancellationToken cancellationToken)
    {
        await DeleteQuietlyAsync(storage, written);
        await transaction.RollbackAsync(cancellationToken);
        return Result.Failure<AttachmentListDto>(error);
    }

    private static async Task DeleteQuietlyAsync(IFileStorage storage, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex) when (ex is StorageUnavailableException or IOException)
            {
                // Best effort — file mồ côi (nếu có) sẽ do job đối soát P2 dọn.
            }
        }
    }
}

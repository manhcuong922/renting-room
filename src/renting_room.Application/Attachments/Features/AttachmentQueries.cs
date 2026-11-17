using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Attachments;
using renting_room.Domain.Common;

namespace renting_room.Application.Attachments;

public sealed record ListAttachmentsQuery(AttachmentOwnerType OwnerType, Guid OwnerId) : IRequest<Result<AttachmentListDto>>;

public sealed class ListAttachmentsHandler(IAttachmentRepository attachments, IAttachmentOwners owners)
    : IRequestHandler<ListAttachmentsQuery, Result<AttachmentListDto>>
{
    public async ValueTask<Result<AttachmentListDto>> Handle(ListAttachmentsQuery request, CancellationToken cancellationToken)
    {
        if (!await owners.ExistsAsync(request.OwnerType, request.OwnerId, cancellationToken))
            return Result.Failure<AttachmentListDto>(AttachmentErrors.OwnerNotFound);

        var items = await attachments.ListActiveByOwnerAsync(request.OwnerType, request.OwnerId, cancellationToken);
        return new AttachmentListDto(items.Select(AttachmentDto.From).ToList());
    }
}

/// <summary>FS-BR-06, 07: tải qua API (đã xác thực + đúng tổ chức); file nhạy cảm ghi audit TRƯỚC khi trả nội dung.</summary>
public sealed record GetAttachmentContentQuery(Guid Id) : IRequest<Result<AttachmentContent>>;

public sealed class GetAttachmentContentHandler(
    IAttachmentRepository attachments, IAttachmentOwners owners, IFileStorageSelector storages, IAuditTrail auditTrail)
    : IRequestHandler<GetAttachmentContentQuery, Result<AttachmentContent>>
{
    public async ValueTask<Result<AttachmentContent>> Handle(GetAttachmentContentQuery request, CancellationToken cancellationToken)
    {
        var attachment = await attachments.GetActiveForUpdateAsync(request.Id, cancellationToken);
        if (attachment is null || !await owners.ExistsAsync(attachment.OwnerType, attachment.OwnerId, cancellationToken))
            return Result.Failure<AttachmentContent>(AttachmentErrors.NotFound);

        Stream stream;
        try
        {
            stream = await storages.For(attachment.StorageProvider).OpenReadAsync(attachment.StorageKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Result.Failure<AttachmentContent>(AttachmentErrors.NotFound);
        }
        catch (StorageUnavailableException)
        {
            return Result.Failure<AttachmentContent>(AttachmentErrors.StorageUnavailable);
        }

        if (attachment.IsSensitive)
        {
            try
            {
                await auditTrail.RecordReadAsync(AuditActions.DownloadAttachment, nameof(Attachment), attachment.Id,
                    new { attachment.OwnerType, attachment.OwnerId, attachment.Category }, cancellationToken);
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
        }

        return new AttachmentContent(stream, attachment.OriginalFileName, attachment.ContentType, attachment.SizeBytes, attachment.IsSensitive);
    }
}

/// <summary>FS-BR-06, FS-BR-16: liên kết tạm 5 phút — chỉ file không nhạy cảm trên kho hỗ trợ (S3).</summary>
public sealed record GetAttachmentSignedUrlQuery(Guid Id) : IRequest<Result<SignedUrlDto>>;

public sealed class GetAttachmentSignedUrlHandler(
    IAttachmentRepository attachments, IAttachmentOwners owners, IFileStorageSelector storages, IStorageSettings settings, TimeProvider clock)
    : IRequestHandler<GetAttachmentSignedUrlQuery, Result<SignedUrlDto>>
{
    public async ValueTask<Result<SignedUrlDto>> Handle(GetAttachmentSignedUrlQuery request, CancellationToken cancellationToken)
    {
        var attachment = await attachments.GetActiveForUpdateAsync(request.Id, cancellationToken);
        if (attachment is null || !await owners.ExistsAsync(attachment.OwnerType, attachment.OwnerId, cancellationToken))
            return Result.Failure<SignedUrlDto>(AttachmentErrors.NotFound);
        if (attachment.IsSensitive)
            return Result.Failure<SignedUrlDto>(AttachmentErrors.SignedUrlNotSupported);

        try
        {
            var url = storages.For(attachment.StorageProvider).GetSignedUrl(attachment.StorageKey, settings.SignedUrlLifetime);
            return url is null
                ? Result.Failure<SignedUrlDto>(AttachmentErrors.SignedUrlNotSupported)
                : new SignedUrlDto(url, clock.GetUtcNow().Add(settings.SignedUrlLifetime));
        }
        catch (StorageUnavailableException)
        {
            return Result.Failure<SignedUrlDto>(AttachmentErrors.StorageUnavailable);
        }
    }
}

/// <summary>FS-BR-08: xóa mềm (file vật lý do job dọn sau 30 ngày); chứng từ tài chính không cho xóa.</summary>
public sealed record DeleteAttachmentCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteAttachmentHandler(
    IAttachmentRepository attachments, IAttachmentOwners owners, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<DeleteAttachmentCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        var attachment = await attachments.GetActiveForUpdateAsync(request.Id, cancellationToken);
        if (attachment is null || !await owners.ExistsAsync(attachment.OwnerType, attachment.OwnerId, cancellationToken))
            return Result.Failure(AttachmentErrors.NotFound);
        if (AttachmentRules.IsDeletionLocked(attachment.Category))
            return Result.Failure(AttachmentErrors.Locked);

        attachment.SoftDelete(clock.GetUtcNow(), currentUser.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

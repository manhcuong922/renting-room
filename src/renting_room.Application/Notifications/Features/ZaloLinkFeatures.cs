using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Notifications;
using renting_room.Domain.Renters;

namespace renting_room.Application.Notifications;

// ---- NT-UC-01: tạo mã liên kết

public sealed record ZaloLinkCodeDto(string Code, DateTimeOffset ExpiresAt, string? BotName);

public sealed record CreateZaloLinkCodeCommand(Guid RenterId) : IRequest<Result<ZaloLinkCodeDto>>;

public sealed class CreateZaloLinkCodeHandler(
    IRenterRepository renters, IZaloLinkCodeRepository codes, INotificationChannels channels, ILinkCodeHasher hasher,
    IUnitOfWork unitOfWork, IAuditTrail auditTrail, TimeProvider clock)
    : IRequestHandler<CreateZaloLinkCodeCommand, Result<ZaloLinkCodeDto>>
{
    public async ValueTask<Result<ZaloLinkCodeDto>> Handle(CreateZaloLinkCodeCommand request, CancellationToken cancellationToken)
    {
        if (!channels.ZaloConfigured)
            return NotificationErrors.ZaloNotConfigured;
        if (await renters.GetForUpdateAsync(request.RenterId, cancellationToken) is not { } renter)
            return RenterErrors.RenterNotFound;
        if (renter.IsAnonymized)
            return RenterErrors.Anonymized;

        var now = clock.GetUtcNow();
        await codes.RevokeUnusedAsync(renter.Id, now, cancellationToken); // NT-BR-13: mã cũ chưa dùng hết hiệu lực
        var (entity, code) = ZaloLinkCode.Create(renter.Id, now, hasher.Hash);
        codes.Add(entity);
        auditTrail.Record("ZaloLinkCodeCreated", nameof(Renter), renter.Id); // không ghi mã
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ZaloLinkCodeDto(code, entity.ExpiresAt, await channels.GetZaloBotNameAsync(cancellationToken));
    }
}

// ---- NT-UC-04: chủ trọ hủy liên kết

public sealed record UnlinkRenterZaloCommand(Guid RenterId) : IRequest<Result>;

public sealed class UnlinkRenterZaloHandler(
    IRenterRepository renters, INotificationRepository notifications, IUnitOfWork unitOfWork, IAuditTrail auditTrail)
    : IRequestHandler<UnlinkRenterZaloCommand, Result>
{
    public async ValueTask<Result> Handle(UnlinkRenterZaloCommand request, CancellationToken cancellationToken)
    {
        if (await renters.GetForUpdateAsync(request.RenterId, cancellationToken) is not { } renter)
            return Result.Failure(RenterErrors.RenterNotFound);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        renter.UnlinkZalo();
        await notifications.CancelPendingZaloForRenterAsync(renter.Id, cancellationToken);
        auditTrail.Record("ZaloUnlinked", nameof(Renter), renter.Id, new { by = "Owner" });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- NT-UC-02 / NT-UC-03: do bot gọi (chạy dưới danh nghĩa hệ thống của tổ chức chứa hồ sơ)

/// <summary>Người thuê nhắn đúng mã ⇒ gắn cuộc chat vào hồ sơ (NT-BR-15: liên kết lại ghi đè).</summary>
public sealed record LinkRenterZaloCommand(Guid RenterId, string ChatId) : IRequest<Result>;

public sealed class LinkRenterZaloHandler(IRenterRepository renters, IUnitOfWork unitOfWork, IAuditTrail auditTrail, TimeProvider clock)
    : IRequestHandler<LinkRenterZaloCommand, Result>
{
    public async ValueTask<Result> Handle(LinkRenterZaloCommand request, CancellationToken cancellationToken)
    {
        if (await renters.GetForUpdateAsync(request.RenterId, cancellationToken) is not { IsAnonymized: false } renter)
            return Result.Failure(RenterErrors.RenterNotFound);

        renter.LinkZalo(request.ChatId, clock.GetUtcNow());
        auditTrail.Record("ZaloLinked", nameof(Renter), renter.Id); // user = hệ thống; không ghi chat id (dữ liệu cá nhân)
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Lệnh <c>HUY</c>: xóa liên kết MỌI hồ sơ gắn với cuộc chat (trong tổ chức này) và hủy tin Zalo đang chờ.</summary>
public sealed record UnlinkZaloByChatCommand(string ChatId, IReadOnlyCollection<Guid> RenterIds) : IRequest<int>;

public sealed class UnlinkZaloByChatHandler(
    IRenterRepository renters, INotificationRepository notifications, IUnitOfWork unitOfWork, IAuditTrail auditTrail)
    : IRequestHandler<UnlinkZaloByChatCommand, int>
{
    public async ValueTask<int> Handle(UnlinkZaloByChatCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var count = 0;
        foreach (var id in request.RenterIds)
        {
            // Chỉ gỡ khi hồ sơ VẪN gắn đúng cuộc chat này (có thể đã liên kết lại với chat khác trong lúc chờ).
            if (await renters.GetForUpdateAsync(id, cancellationToken) is not { } renter || renter.ZaloChatId != request.ChatId)
                continue;
            renter.UnlinkZalo();
            await notifications.CancelPendingZaloForRenterAsync(id, cancellationToken);
            auditTrail.Record("ZaloUnlinked", nameof(Renter), id, new { by = "Renter" });
            count++;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }
}

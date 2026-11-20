using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Notifications;
using renting_room.Domain.Properties;

namespace renting_room.Application.Notifications;

// ---- NT-UC-12: gửi thông báo chung cho một khu (M11 §17)

/// <param name="RoomIds">null / rỗng = mọi phòng đang thuê của khu.</param>
public sealed record SendAnnouncementCommand(Guid PropertyId, string Title, string Body, IReadOnlyList<Guid>? RoomIds)
    : IRequest<Result<AnnouncementSentDto>>;

public sealed record AnnouncementRoomDto(Guid RoomId, string RoomCode);

/// <param name="RoomCount">Số phòng có người đứng tên (HĐ đang hiệu lực) trong phạm vi gửi.</param>
/// <param name="Queued">Số tin đã xếp hàng (sau khi gộp theo chat Zalo).</param>
/// <param name="NoChannelRooms">Phòng có người đứng tên nhưng chưa liên kết Zalo — chủ trọ nhắn tay.</param>
public sealed record AnnouncementSentDto(Guid Id, string Title, int RoomCount, int Queued, IReadOnlyList<AnnouncementRoomDto> NoChannelRooms);

public sealed class SendAnnouncementCommandValidator : AbstractValidator<SendAnnouncementCommand>
{
    public const int MaxRooms = 500;

    public SendAnnouncementCommandValidator()
    {
        RuleFor(x => x.Title).RequiredText(Announcement.TitleMaxLength, "Tiêu đề");
        RuleFor(x => x.Body).RequiredText(Announcement.BodyMaxLength, "Nội dung");
        RuleFor(x => x.RoomIds)
            .Must(ids => ids!.Count <= MaxRooms).WithErrorCode("MAX_ITEMS").WithMessage($"Chọn tối đa {MaxRooms} phòng.")
            .Must(ids => ids!.Distinct().Count() == ids!.Count).WithErrorCode("DUPLICATE").WithMessage("Danh sách phòng bị trùng.")
            .Must(ids => !ids!.Contains(Guid.Empty)).WithErrorCode("REQUIRED").WithMessage("Mã phòng không hợp lệ.")
            .When(x => x.RoomIds is { Count: > 0 });
    }
}

public sealed class SendAnnouncementHandler(
    IPropertyQueries properties, IAnnouncementRepository announcements, INotificationRepository notifications, INotificationChannels channels,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<SendAnnouncementCommand, Result<AnnouncementSentDto>>
{
    public async ValueTask<Result<AnnouncementSentDto>> Handle(SendAnnouncementCommand request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;
        if (!channels.ZaloConfigured)
            return NotificationErrors.ZaloNotConfigured;

        var roomIds = request.RoomIds is { Count: > 0 } selected ? selected : null;
        if (roomIds is not null && await announcements.CountRoomsAsync(request.PropertyId, roomIds, cancellationToken) != roomIds.Count)
            return PropertyErrors.RoomNotFound;

        var targets = await announcements.ListTargetsAsync(request.PropertyId, roomIds, cancellationToken);
        var plan = AnnouncementRecipients.Plan(targets);
        if (plan.Recipients.Count == 0)
            return NotificationErrors.NoChannel;

        var announcement = Announcement.Create(
            request.PropertyId, request.Title, request.Body, allRooms: roomIds is null, plan.RoomCount, plan.Recipients.Count);
        announcements.Add(announcement);
        var now = clock.GetUtcNow();
        foreach (var recipient in plan.Recipients)
            notifications.Add(NotificationOutbox.CreateForAnnouncement(announcement.Id, recipient.RenterId, recipient.ZaloChatId!, now));
        await unitOfWork.SaveChangesAsync(cancellationToken); // thông báo + mọi tin trong một lần lưu (một transaction)

        return new AnnouncementSentDto(announcement.Id, announcement.Title, plan.RoomCount, plan.Recipients.Count, plan.NoChannelRooms);
    }
}

/// <summary>NT-BR-20: chọn người nhận từ danh sách người đứng tên theo phòng — bỏ hồ sơ ẩn danh / chưa liên kết, gộp theo chat Zalo.</summary>
public static class AnnouncementRecipients
{
    public sealed record RecipientPlan(int RoomCount, IReadOnlyList<AnnouncementTarget> Recipients, IReadOnlyList<AnnouncementRoomDto> NoChannelRooms);

    public static RecipientPlan Plan(IReadOnlyList<AnnouncementTarget> targets)
    {
        var ordered = targets.OrderBy(t => t.RoomCode, StringComparer.Ordinal).ToList();
        static bool Reachable(AnnouncementTarget t) => !t.IsAnonymized && !string.IsNullOrEmpty(t.ZaloChatId);

        var recipients = ordered.Where(Reachable).DistinctBy(t => t.ZaloChatId).ToList();
        var noChannel = ordered.GroupBy(t => t.RoomId)
            .Where(g => !g.Any(Reachable))
            .Select(g => new AnnouncementRoomDto(g.Key, g.First().RoomCode))
            .ToList();
        return new RecipientPlan(ordered.Select(t => t.RoomId).Distinct().Count(), recipients, noChannel);
    }
}

// ---- NT-UC-13: các thông báo chung đã gửi của khu

public sealed record ListAnnouncementsQuery(Guid PropertyId, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<Result<PagedResult<AnnouncementDto>>>;

/// <param name="Pending">Đang chờ + đang gửi.</param>
public sealed record AnnouncementDto(
    Guid Id, string Title, string Body, bool AllRooms, int RoomCount, int RecipientCount, int Pending, int Sent, int Failed, int Cancelled,
    DateTimeOffset CreatedAt);

public sealed class ListAnnouncementsQueryValidator : AbstractValidator<ListAnnouncementsQuery>
{
    public ListAnnouncementsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
    }
}

public sealed class ListAnnouncementsHandler(IPropertyQueries properties, IAnnouncementRepository announcements)
    : IRequestHandler<ListAnnouncementsQuery, Result<PagedResult<AnnouncementDto>>>
{
    public async ValueTask<Result<PagedResult<AnnouncementDto>>> Handle(ListAnnouncementsQuery request, CancellationToken cancellationToken) =>
        await properties.ExistsAsync(request.PropertyId, cancellationToken)
            ? Result.Success(await announcements.ListAsync(request.PropertyId, request.Page, request.PageSize, cancellationToken))
            : PropertyErrors.PropertyNotFound;
}

/// <summary>NT-BR-23: <c>[Tên khu] Tiêu đề</c>, dòng trống, nội dung, dòng trống, cách ngừng nhận tin. Văn bản thuần.</summary>
public static class AnnouncementMessageBuilder
{
    /// <summary>Tên khu dài bất thường bị cắt để tổng không vượt giới hạn Zalo (tiêu đề + nội dung đã giới hạn ở NT-BR-21).</summary>
    private const int PropertyNameMaxLength = 200;

    public static string Build(string propertyName, string title, string body, bool includeUnsubscribeHint = true)
    {
        var name = propertyName.Length > PropertyNameMaxLength ? propertyName[..PropertyNameMaxLength] : propertyName;
        var text = $"[{name}] {title.Trim()}\n\n{body.Trim()}";
        return includeUnsubscribeHint ? $"{text}\n\n{InvoiceMessageBuilder.UnsubscribeHint}" : text;
    }
}

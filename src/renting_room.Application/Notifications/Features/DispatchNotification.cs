using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Notifications;

namespace renting_room.Application.Notifications;

/// <param name="Outcome">null = tin không còn ở trạng thái đang gửi (đã xử lý / bị hủy) — không làm gì.</param>
public sealed record DispatchResult(NotificationChannel Channel, SendOutcome? Outcome, TimeSpan? RetryAfter);

/// <summary>
/// Gửi MỘT tin đã được dispatcher nhận (<c>Sending</c>), trong scope của tổ chức chứa tin. Đọc lại phiếu ngay trước khi gửi: nội dung luôn mới nhất
/// (NT-BR-17 dựng lại, không lưu) và nhắc nợ cho phiếu đã hết nợ / đã hủy thì bỏ qua (NT-BR-07).
/// </summary>
public sealed record DispatchNotificationCommand(Guid OutboxId) : IRequest<DispatchResult>;

public sealed class DispatchNotificationHandler(
    INotificationRepository notifications, IInvoiceQueries invoices, IPropertyQueries properties, IRenterRepository renters,
    IAnnouncementRepository announcements, IEnumerable<INotificationSender> senders, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<DispatchNotificationCommand, DispatchResult>
{
    public async ValueTask<DispatchResult> Handle(DispatchNotificationCommand request, CancellationToken cancellationToken)
    {
        if (await notifications.GetForUpdateAsync(request.OutboxId, cancellationToken) is not { Status: NotificationStatus.Sending } outbox)
            return new DispatchResult(NotificationChannel.Zalo, null, null);

        var result = outbox.AnnouncementId is { } announcementId
            ? await DecideAnnouncementAsync(outbox, announcementId, cancellationToken)
            : await DecideAsync(outbox, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new DispatchResult(outbox.Channel, result?.Outcome, result?.RetryAfter);
    }

    /// <returns>Kết quả gửi thật; null nếu tin bị bỏ qua / thất bại trước khi gửi.</returns>
    private async Task<SendResult?> DecideAsync(NotificationOutbox outbox, CancellationToken ct)
    {
        var invoice = outbox.InvoiceId is { } invoiceId ? await invoices.GetDetailsAsync(invoiceId, ct) : null;
        if (invoice is null || invoice.Status != InvoiceStatus.Finalized || invoice.VoidedAt is not null)
        {
            outbox.MarkSkipped(); // phiếu đã hủy trong lúc tin chờ — không gửi nội dung của phiếu hủy
            return null;
        }
        if (outbox.Kind == NotificationKind.OverdueReminder && invoice.Outstanding <= 0)
        {
            outbox.MarkSkipped();
            return null;
        }
        if (ResolveSender(outbox) is not { } sender)
            return null;

        var property = await properties.GetAsync(invoice.PropertyId, ct);
        var content = InvoiceMessageBuilder.Build(
            invoice, property?.Name ?? string.Empty, property?.BankAccount, outbox.Kind, outbox.Step, clock.GetUtcNow().ToBusinessDate(),
            includeUnsubscribeHint: outbox.Channel == NotificationChannel.Zalo);
        return await SendAndRecordAsync(outbox, sender, content.Subject, content.Text, ct);
    }

    /// <summary>M11 §17 / NT-BR-23: nội dung lấy từ thông báo đã lưu; thông báo không còn ⇒ bỏ qua.</summary>
    private async Task<SendResult?> DecideAnnouncementAsync(NotificationOutbox outbox, Guid announcementId, CancellationToken ct)
    {
        if (await announcements.GetAsync(announcementId, ct) is not { } announcement)
        {
            outbox.MarkSkipped();
            return null;
        }
        if (ResolveSender(outbox) is not { } sender)
            return null;

        var property = await properties.GetAsync(announcement.PropertyId, ct);
        var text = AnnouncementMessageBuilder.Build(property?.Name ?? string.Empty, announcement.Title, announcement.Body,
            includeUnsubscribeHint: outbox.Channel == NotificationChannel.Zalo);
        return await SendAndRecordAsync(outbox, sender, announcement.Title, text, ct);
    }

    /// <returns>null (và tin đã <c>Failed</c>) nếu thiếu người nhận hoặc kênh chưa cấu hình ở máy chủ.</returns>
    private INotificationSender? ResolveSender(NotificationOutbox outbox)
    {
        if (outbox.Recipient is null)
        {
            outbox.MarkFailed("NO_RECIPIENT");
            return null;
        }
        var sender = senders.FirstOrDefault(s => s.Channel == outbox.Channel);
        if (sender is { IsConfigured: true })
            return sender;
        outbox.MarkFailed("CHANNEL_NOT_CONFIGURED");
        return null;
    }

    private async Task<SendResult> SendAndRecordAsync(NotificationOutbox outbox, INotificationSender sender, string subject, string text, CancellationToken ct)
    {
        var result = await sender.SendAsync(new NotificationMessage(outbox.Recipient!, subject, text), ct);

        var now = clock.GetUtcNow();
        switch (result.Outcome)
        {
            case SendOutcome.Ok:
                outbox.MarkSent(now, result.ProviderMessageId);
                break;
            case SendOutcome.Transient:
                outbox.ScheduleRetry(now, result.ErrorCode ?? "TRANSIENT", result.RetryAfter);
                break;
            case SendOutcome.RecipientUnreachable:
                outbox.MarkFailed(result.ErrorCode ?? "RECIPIENT_UNREACHABLE");
                // NT-BR-10: người dùng chặn bot / chat không tồn tại ⇒ xóa liên kết để khỏi gửi tiếp vô ích.
                if (outbox.Channel == NotificationChannel.Zalo && await renters.GetForUpdateAsync(outbox.RenterId, ct) is { } renter
                    && renter.ZaloChatId == outbox.Recipient)
                {
                    renter.UnlinkZalo();
                    await notifications.CancelPendingZaloForRenterAsync(renter.Id, ct);
                }
                break;
            default:
                outbox.MarkFailed(result.ErrorCode ?? "PERMANENT");
                break;
        }
        return result;
    }
}

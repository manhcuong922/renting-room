using renting_room.Domain.Billing;
using renting_room.Domain.Notifications;

namespace renting_room.Application.Notifications;

/// <summary>
/// M11 NT-BR-01..05: tạo dòng outbox cho phiếu. KHÔNG gọi SaveChanges — dòng đi cùng transaction của lệnh gọi (chốt phiếu, gửi lại, job nhắc nợ),
/// nên chốt phiếu thành công thì chắc chắn có tin (NT-BR-04).
/// </summary>
public sealed class NotificationEnqueuer(INotificationRepository outbox, INotificationChannels channels, TimeProvider clock)
{
    /// <summary>NT-BR-01: chỉ phiếu đã chốt, chưa hủy, tổng &gt; 0 — phiếu hoàn trả / phiếu 0 đồng không gửi tự động.</summary>
    public static bool IsSendable(Invoice invoice) =>
        invoice is { Status: InvoiceStatus.Finalized, VoidedAt: null, TotalAmount: > 0 };

    /// <summary>NT-UC-05: chốt phiếu ở khu bật "tự gửi khi chốt". Trả số tin đã tạo.</summary>
    public async Task<int> EnqueueIssuedAsync(Invoice invoice, Guid representativeRenterId, CancellationToken cancellationToken)
    {
        if (!IsSendable(invoice) || !await outbox.GetNotifyOnFinalizeAsync(invoice.PropertyId, cancellationToken))
            return 0;
        return (await EnqueueAsync(invoice.Id, representativeRenterId, NotificationKind.InvoiceIssued, 0, only: null, cancellationToken)).Count;
    }

    /// <summary>
    /// Tạo tin cho mọi kênh người nhận có và máy chủ đã cấu hình (NT-BR-03). <paramref name="only"/> null = mọi kênh.
    /// Hồ sơ đã ẩn danh ⇒ không tạo tin (NT-BR-02).
    /// </summary>
    public async Task<IReadOnlyList<NotificationOutbox>> EnqueueAsync(
        Guid invoiceId, Guid renterId, NotificationKind kind, int step, IReadOnlyCollection<NotificationChannel>? only, CancellationToken cancellationToken)
    {
        if (await outbox.GetRecipientAsync(renterId, cancellationToken) is not { IsAnonymized: false } recipient)
            return [];

        var created = new List<NotificationOutbox>();
        var now = clock.GetUtcNow();
        foreach (var (channel, address) in new[]
                 {
                     (NotificationChannel.Zalo, recipient.ZaloChatId),
                     (NotificationChannel.Email, recipient.Email)
                 })
        {
            if (address is null || !channels.IsConfigured(channel) || (only is not null && !only.Contains(channel)))
                continue;
            var row = NotificationOutbox.Create(invoiceId, renterId, channel, kind, step, address, now);
            outbox.Add(row);
            created.Add(row);
        }
        return created;
    }
}

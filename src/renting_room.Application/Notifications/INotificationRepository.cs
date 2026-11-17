using renting_room.Domain.Notifications;

namespace renting_room.Application.Notifications;

/// <summary>Người nhận của một phiếu: chat Zalo / email hiện có và hồ sơ đã ẩn danh chưa (NT-BR-02).</summary>
public sealed record NotificationRecipient(Guid RenterId, string? ZaloChatId, string? Email, bool IsAnonymized);

public sealed record NotificationDto(
    Guid Id, string Channel, string Kind, int Step, string Status, int Attempts, string? LastError, DateTimeOffset? SentAt, DateTimeOffset CreatedAt);

/// <summary>Truy cập trong phạm vi một tổ chức (global filter). Các truy vấn xuyên tổ chức nằm ở <c>NotificationOutboxStore</c> / <c>ZaloLinkCodeStore</c>.</summary>
public interface INotificationRepository
{
    void Add(NotificationOutbox outbox);

    Task<NotificationOutbox?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDto>> ListByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Số thứ tự cho lần gửi lại kế tiếp (NT-BR-05: <c>UNIQUE (invoice_id, channel, kind, step)</c>).</summary>
    Task<int> NextManualStepAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Các bước nhắc nợ đã có tin (mọi trạng thái trừ <c>Cancelled</c>) của phiếu.</summary>
    Task<IReadOnlyList<int>> ListReminderStepsAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<NotificationRecipient?> GetRecipientAsync(Guid renterId, CancellationToken cancellationToken);

    /// <summary>Cài đặt thông báo của khu: <c>notify_on_finalize</c> (NT-BR-04).</summary>
    Task<bool> GetNotifyOnFinalizeAsync(Guid propertyId, CancellationToken cancellationToken);

    /// <summary>NT-BR-12: hủy phiếu ⇒ mọi tin <c>Pending</c> của phiếu thành <c>Cancelled</c> (cùng transaction).</summary>
    Task<int> CancelPendingForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>NT-UC-03 / NT-UC-04: hủy liên kết Zalo ⇒ tin Zalo đang chờ của người đó thành <c>Cancelled</c>.</summary>
    Task<int> CancelPendingZaloForRenterAsync(Guid renterId, CancellationToken cancellationToken);

    /// <summary>NT-BR-16: ẩn danh ⇒ hủy tin chờ, xóa <c>recipient</c> trong mọi dòng outbox và thu hồi mã liên kết của người đó.</summary>
    Task PurgeRenterAsync(Guid renterId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Phiếu quá hạn cần xét nhắc nợ trong tổ chức hiện tại (NT-BR-06).</summary>
    Task<IReadOnlyList<OverdueInvoiceRow>> ListOverdueAsync(DateOnly today, CancellationToken cancellationToken);

    Task<NotificationSummaryDto> GetSummaryAsync(Guid propertyId, DateOnly billingMonth, CancellationToken cancellationToken);
}

public sealed record OverdueInvoiceRow(
    Guid InvoiceId, Guid ContractId, Guid RenterId, DateOnly DueDate, IReadOnlyList<int> ConfiguredDays, IReadOnlyList<int> SentSteps);

public sealed record NotificationSummaryDto(int Total, int Pending, int Sent, int Failed, int Skipped, int Cancelled, int NoChannel);

/// <summary>Mã liên kết trong phạm vi tổ chức hiện tại.</summary>
public interface IZaloLinkCodeRepository
{
    void Add(ZaloLinkCode code);

    /// <summary>NT-BR-13: mã cũ chưa dùng của người thuê hết hiệu lực khi tạo mã mới.</summary>
    Task RevokeUnusedAsync(Guid renterId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Hai truy vấn xuyên tổ chức của bot (M11 §6): bot không biết tổ chức ⇒ tra mã theo hash trên toàn hệ thống, và tìm hồ sơ theo chat id.
/// Mọi thao tác sau đó chạy trong scope của đúng một tổ chức.
/// </summary>
public interface IZaloLinkLookup
{
    Task<ZaloLinkTarget?> FindUsableAsync(string codeHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Dùng mã một lần, nguyên tử: chỉ một bên thành công khi hai người nhập cùng mã (§10).</summary>
    Task<bool> TryConsumeAsync(Guid codeId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<ZaloChatRenter>> ListByChatAsync(string chatId, CancellationToken cancellationToken);
}

public sealed record ZaloLinkTarget(Guid CodeId, Guid OrganizationId, Guid RenterId);

public sealed record ZaloChatRenter(Guid OrganizationId, Guid RenterId);

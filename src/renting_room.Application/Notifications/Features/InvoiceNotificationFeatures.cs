using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Notifications;
using renting_room.Domain.Properties;

namespace renting_room.Application.Notifications;

// ---- NT-UC-11: kênh nào đang dùng được

public sealed record GetNotificationChannelsQuery : IRequest<NotificationChannelsDto>;

public sealed record ZaloChannelDto(bool Configured, string? BotName);

public sealed record EmailChannelDto(bool Configured);

public sealed record NotificationChannelsDto(ZaloChannelDto Zalo, EmailChannelDto Email);

public sealed class GetNotificationChannelsHandler(INotificationChannels channels) : IRequestHandler<GetNotificationChannelsQuery, NotificationChannelsDto>
{
    public async ValueTask<NotificationChannelsDto> Handle(GetNotificationChannelsQuery request, CancellationToken cancellationToken) =>
        new(new ZaloChannelDto(channels.ZaloConfigured, channels.ZaloConfigured ? await channels.GetZaloBotNameAsync(cancellationToken) : null),
            new EmailChannelDto(channels.EmailConfigured));
}

// ---- NT-UC-08: lịch sử gửi của một phiếu

public sealed record GetInvoiceNotificationsQuery(Guid InvoiceId) : IRequest<Result<IReadOnlyList<NotificationDto>>>;

public sealed class GetInvoiceNotificationsHandler(IInvoiceQueries invoices, INotificationRepository notifications)
    : IRequestHandler<GetInvoiceNotificationsQuery, Result<IReadOnlyList<NotificationDto>>>
{
    public async ValueTask<Result<IReadOnlyList<NotificationDto>>> Handle(GetInvoiceNotificationsQuery request, CancellationToken cancellationToken) =>
        await invoices.GetAsync(request.InvoiceId, cancellationToken) is null
            ? BillingErrors.NotFound
            : Result.Success(await notifications.ListByInvoiceAsync(request.InvoiceId, cancellationToken));
}

// ---- NT-UC-08: tổng hợp theo khu + tháng thu

public sealed record GetNotificationSummaryQuery(Guid PropertyId, DateOnly BillingMonth) : IRequest<Result<NotificationSummaryDto>>;

public sealed class GetNotificationSummaryHandler(IPropertyQueries properties, INotificationRepository notifications)
    : IRequestHandler<GetNotificationSummaryQuery, Result<NotificationSummaryDto>>
{
    public async ValueTask<Result<NotificationSummaryDto>> Handle(GetNotificationSummaryQuery request, CancellationToken cancellationToken) =>
        request.BillingMonth.Day != 1
            ? NotificationErrors.InvalidBillingMonth
            : await properties.ExistsAsync(request.PropertyId, cancellationToken)
            ? Result.Success(await notifications.GetSummaryAsync(request.PropertyId, request.BillingMonth, cancellationToken))
            : PropertyErrors.PropertyNotFound;
}

// ---- NT-UC-07: gửi lại thủ công

public sealed record ResendInvoiceNotificationCommand(Guid InvoiceId, IReadOnlyList<string>? Channels)
    : IRequest<Result<IReadOnlyList<NotificationDto>>>;

public sealed class ResendInvoiceNotificationHandler(
    IUnitOfWork unitOfWork, IInvoiceQueries invoices, IContractQueries contracts, INotificationRepository notifications,
    NotificationEnqueuer enqueuer, IAuditTrail auditTrail)
    : IRequestHandler<ResendInvoiceNotificationCommand, Result<IReadOnlyList<NotificationDto>>>
{
    public async ValueTask<Result<IReadOnlyList<NotificationDto>>> Handle(ResendInvoiceNotificationCommand request, CancellationToken cancellationToken)
    {
        if (request.Channels is { } requested
            && (requested.Distinct().Count() != requested.Count || !requested.All(c => Enum.TryParse<NotificationChannel>(c, out _))))
            return NotificationErrors.InvalidChannel;
        if (await invoices.GetAsync(request.InvoiceId, cancellationToken) is not { } invoice)
            return BillingErrors.NotFound;
        if (invoice.Status == InvoiceStatus.Void || invoice.VoidedAt is not null)
            return NotificationErrors.InvoiceVoided;
        if (invoice.Status != InvoiceStatus.Finalized)
            return NotificationErrors.InvoiceNotFinalized;

        var contract = (await contracts.GetAsync(invoice.ContractId, cancellationToken))!;
        var only = request.Channels?.Select(Enum.Parse<NotificationChannel>).ToList();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        // Hai lần bấm gần nhau không được cùng lấy một số thứ tự (UNIQUE invoice_id, channel, kind, step).
        await unitOfWork.LockForUpdateAsync<Invoice>(invoice.Id, cancellationToken);
        var step = await notifications.NextManualStepAsync(invoice.Id, cancellationToken);
        var created = await enqueuer.EnqueueAsync(invoice.Id, contract.RepresentativeRenterId, NotificationKind.ManualResend, step, only, cancellationToken);
        if (created.Count == 0)
            return NotificationErrors.NoChannel;

        auditTrail.Record("NotificationResent", nameof(Invoice), invoice.Id,
            new { channels = created.Select(c => c.Channel.ToString()).ToArray(), step });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created.Select(c => c.ToDto()).ToList();
    }
}

public static class NotificationMapping
{
    public static NotificationDto ToDto(this NotificationOutbox n) =>
        new(n.Id, n.Channel.ToString(), n.Kind.ToString(), n.Step, n.Status.ToString(), n.Attempts, n.LastError, n.SentAt, n.CreatedAt);
}

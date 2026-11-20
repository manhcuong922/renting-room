using Microsoft.EntityFrameworkCore;
using renting_room.Application.Notifications;
using renting_room.Application.Common.Models;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Notifications;

namespace renting_room.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository(AppDbContext db) : INotificationRepository
{
    public void Add(NotificationOutbox outbox) => db.NotificationOutbox.Add(outbox);

    public Task<NotificationOutbox?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        db.NotificationOutbox.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<IReadOnlyList<NotificationDto>> ListByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        (await db.NotificationOutbox.AsNoTracking().Where(n => n.InvoiceId == invoiceId)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Channel).ToListAsync(cancellationToken))
        .Select(n => n.ToDto()).ToList();

    public async Task<int> NextManualStepAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        1 + await db.NotificationOutbox.Where(n => n.InvoiceId == invoiceId && n.Kind == NotificationKind.ManualResend)
            .Select(n => (int?)n.Step).MaxAsync(cancellationToken) ?? 1;

    public async Task<IReadOnlyList<int>> ListReminderStepsAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.NotificationOutbox.Where(n => n.InvoiceId == invoiceId && n.Kind == NotificationKind.OverdueReminder
                && n.Status != NotificationStatus.Cancelled)
            .Select(n => n.Step).Distinct().ToListAsync(cancellationToken);

    public Task<NotificationRecipient?> GetRecipientAsync(Guid renterId, CancellationToken cancellationToken) =>
        db.Renters.AsNoTracking().Where(r => r.Id == renterId)
            .Select(r => new NotificationRecipient(r.Id, r.ZaloChatId, r.Email, r.AnonymizedAt != null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> GetNotifyOnFinalizeAsync(Guid propertyId, CancellationToken cancellationToken) =>
        db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => p.NotifyOnFinalize).FirstOrDefaultAsync(cancellationToken);

    public Task<int> CancelPendingForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        db.NotificationOutbox.Where(n => n.InvoiceId == invoiceId && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Cancelled), cancellationToken);

    public Task<int> CancelPendingZaloForRenterAsync(Guid renterId, CancellationToken cancellationToken) =>
        db.NotificationOutbox
            .Where(n => n.RenterId == renterId && n.Channel == NotificationChannel.Zalo && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Cancelled), cancellationToken);

    public async Task PurgeRenterAsync(Guid renterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.NotificationOutbox.Where(n => n.RenterId == renterId && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Cancelled), cancellationToken);
        await db.NotificationOutbox.Where(n => n.RenterId == renterId && n.Recipient != null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Recipient, (string?)null), cancellationToken);
        await db.ZaloLinkCodes.Where(c => c.RenterId == renterId && c.UsedAt == null && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now), cancellationToken);
    }

    public async Task<IReadOnlyList<OverdueInvoiceRow>> ListOverdueAsync(DateOnly today, CancellationToken cancellationToken)
    {
        // Mảng ngày nhắc nằm sau value converter ⇒ lọc trong bộ nhớ (một tổ chức chỉ có vài khu).
        var configured = (await db.Properties.AsNoTracking().Where(p => p.ArchivedAt == null)
                .Select(p => new { p.Id, p.OverdueReminderDays }).ToListAsync(cancellationToken))
            .Where(p => p.OverdueReminderDays.Count > 0).ToDictionary(p => p.Id, p => p.OverdueReminderDays);
        if (configured.Count == 0)
            return [];

        var propertyIds = configured.Keys.ToList();
        var invoices = await (from i in db.Invoices.AsNoTracking()
                join c in db.Contracts.AsNoTracking() on i.ContractId equals c.Id
                where propertyIds.Contains(i.PropertyId) && i.Status == InvoiceStatus.Finalized && i.VoidedAt == null
                    && i.DueDate != null && i.DueDate < today && i.TotalAmount > i.PaidAmount
                select new { i.Id, i.PropertyId, i.ContractId, c.RepresentativeRenterId, DueDate = i.DueDate!.Value })
            .ToListAsync(cancellationToken);
        var ids = invoices.Select(i => i.Id).ToList();
        var steps = (await db.NotificationOutbox.AsNoTracking()
                .Where(n => n.InvoiceId != null && ids.Contains(n.InvoiceId.Value) && n.Kind == NotificationKind.OverdueReminder
                    && n.Status != NotificationStatus.Cancelled)
                .Select(n => new { InvoiceId = n.InvoiceId!.Value, n.Step }).ToListAsync(cancellationToken))
            .GroupBy(n => n.InvoiceId).ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(n => n.Step).Distinct().ToList());

        return invoices.Select(i => new OverdueInvoiceRow(
            i.Id, i.ContractId, i.RepresentativeRenterId, i.DueDate, configured[i.PropertyId], steps.GetValueOrDefault(i.Id, []))).ToList();
    }

    public async Task<NotificationSummaryDto> GetSummaryAsync(Guid propertyId, DateOnly billingMonth, CancellationToken cancellationToken)
    {
        var month = db.Invoices.AsNoTracking().Where(i => i.PropertyId == propertyId && i.BillingMonth == billingMonth);
        var counts = await (from n in db.NotificationOutbox.AsNoTracking()
                join i in month on n.InvoiceId equals (Guid?)i.Id
                group n by n.Status into g
                select new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
        var notifyOn = await GetNotifyOnFinalizeAsync(propertyId, cancellationToken);
        var noChannel = !notifyOn
            ? 0
            : await month.CountAsync(i => i.Status == InvoiceStatus.Finalized && i.VoidedAt == null && i.TotalAmount > 0
                && !db.NotificationOutbox.Any(n => n.InvoiceId == i.Id), cancellationToken);
        int Of(NotificationStatus s) => counts.GetValueOrDefault(s);
        return new NotificationSummaryDto(
            counts.Values.Sum(), Of(NotificationStatus.Pending) + Of(NotificationStatus.Sending), Of(NotificationStatus.Sent),
            Of(NotificationStatus.Failed), Of(NotificationStatus.Skipped), Of(NotificationStatus.Cancelled), noChannel);
    }
}

internal sealed class ZaloLinkCodeRepository(AppDbContext db) : IZaloLinkCodeRepository
{
    public void Add(ZaloLinkCode code) => db.ZaloLinkCodes.Add(code);

    public Task RevokeUnusedAsync(Guid renterId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.ZaloLinkCodes.Where(c => c.RenterId == renterId && c.UsedAt == null && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now), cancellationToken);
}

/// <summary>M11 §17: thông báo chung — mọi truy vấn qua global filter (tổ chức + khu được giao).</summary>
internal sealed class AnnouncementRepository(AppDbContext db) : IAnnouncementRepository
{
    public void Add(Announcement announcement) => db.Announcements.Add(announcement);

    public Task<Announcement?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Announcements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<int> CountRoomsAsync(Guid propertyId, IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        db.Rooms.CountAsync(r => r.PropertyId == propertyId && roomIds.Contains(r.Id), cancellationToken);

    public async Task<IReadOnlyList<AnnouncementTarget>> ListTargetsAsync(
        Guid propertyId, IReadOnlyCollection<Guid>? roomIds, CancellationToken cancellationToken)
    {
        var contracts = db.Contracts.AsNoTracking()
            .Where(c => c.PropertyId == propertyId && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating));
        if (roomIds is not null)
            contracts = contracts.Where(c => roomIds.Contains(c.RoomId));
        return await (from c in contracts
                join room in db.Rooms.AsNoTracking() on c.RoomId equals room.Id
                join renter in db.Renters.AsNoTracking() on c.RepresentativeRenterId equals renter.Id
                select new AnnouncementTarget(room.Id, room.Code, renter.Id, renter.ZaloChatId, renter.AnonymizedAt != null))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AnnouncementDto>> ListAsync(Guid propertyId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Announcements.AsNoTracking().Where(a => a.PropertyId == propertyId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = items.Select(a => a.Id).ToList();
        var counts = (await db.NotificationOutbox.AsNoTracking()
                .Where(n => n.AnnouncementId != null && ids.Contains(n.AnnouncementId.Value))
                .GroupBy(n => new { AnnouncementId = n.AnnouncementId!.Value, n.Status })
                .Select(g => new { g.Key.AnnouncementId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.AnnouncementId);

        return new PagedResult<AnnouncementDto>(items.Select(a =>
        {
            int Of(params NotificationStatus[] statuses) => counts[a.Id].Where(c => statuses.Contains(c.Status)).Sum(c => c.Count);
            return new AnnouncementDto(a.Id, a.Title, a.Body, a.AllRooms, a.RoomCount, a.RecipientCount,
                Of(NotificationStatus.Pending, NotificationStatus.Sending), Of(NotificationStatus.Sent), Of(NotificationStatus.Failed),
                Of(NotificationStatus.Cancelled), a.CreatedAt);
        }).ToList(), page, pageSize, total);
    }
}

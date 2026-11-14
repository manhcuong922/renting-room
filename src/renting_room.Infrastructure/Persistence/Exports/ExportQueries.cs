using Microsoft.EntityFrameworkCore;
using renting_room.Application.Exports;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class ExportQueries(AppDbContext db) : IExportQueries
{
    private static readonly ContractStatus[] LivedStatuses = [ContractStatus.Active, ContractStatus.Liquidating, ContractStatus.Ended];

    public Task<int> CountRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Renters(request, from, to).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<RenterExportRow>> ListRentersAsync(
        ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await Renters(request, from, to).ToListAsync(cancellationToken);

    private IQueryable<RenterExportRow> Renters(ExportRentersQuery request, DateOnly fromDate, DateOnly toDate)
    {
        var query =
            from c in db.Contracts
            where LivedStatuses.Contains(c.Status)
            from o in c.Occupants
            // Đang thanh lý: ngày chuyển đi chỉ ghi khi hoàn tất ⇒ dùng ngày trả phòng của HĐ.
            let moveOut = o.MoveOutDate ?? c.ActualEndDate
            where o.MoveInDate <= toDate && (moveOut == null || moveOut >= fromDate)
            join r in db.Renters on o.RenterId equals r.Id
            join room in db.Rooms on c.RoomId equals room.Id
            join p in db.Properties on c.PropertyId equals p.Id
            select new RenterExportRow
            {
                PropertyId = p.Id, PropertyCode = p.Code, PropertyName = p.Name,
                RoomId = room.Id, RoomCode = room.Code, Floor = room.Floor,
                ContractNo = c.ContractNo, ContractStatus = c.Status, IsRepresentative = c.RepresentativeRenterId == r.Id,
                IsCoTenant = o.Role == OccupantRole.CoTenant,
                IsHouseholdHead = (c.HouseholdHeadRenterId ?? c.RepresentativeRenterId) == r.Id,
                FullName = r.FullName, DateOfBirth = r.DateOfBirth, Gender = r.Gender, Phone = r.Phone,
                IdType = r.IdType, IdNumberLast4 = r.IdNumberLast4, IdNumberEncrypted = r.IdNumberEncrypted,
                IdIssueDate = r.IdIssueDate, IdIssuePlace = r.IdIssuePlace, Nationality = r.Nationality,
                PermanentAddress = r.PermanentAddress, Occupation = r.Occupation, Workplace = r.Workplace,
                Relationship = o.Relationship, RelationshipType = o.RelationshipType, GuardianConsent = o.GuardianConsent,
                MoveInDate = o.MoveInDate, MoveOutDate = moveOut,
                EmergencyContactName = r.EmergencyContactName, EmergencyContactPhone = r.EmergencyContactPhone
            };

        if (request.PropertyIds is { Count: > 0 } propertyIds)
            query = query.Where(x => propertyIds.Contains(x.PropertyId));
        if (request.RoomIds is { Count: > 0 } roomIds)
            query = query.Where(x => roomIds.Contains(x.RoomId));
        if (request.Floors is { Count: > 0 } floors)
        {
            var normalized = floors.Select(f => f.Trim()).ToList();
            query = query.Where(x => x.Floor != null && normalized.Contains(x.Floor));
        }
        if (request.RoomGroupIds is { Count: > 0 } groupIds)
            query = query.Where(x => db.RoomGroups.Any(g => groupIds.Contains(g.Id) && g.Members.Any(m => m.RoomId == x.RoomId)));

        return query;
    }

    public Task<int> CountInvoicesAsync(ExportMonthlyInvoicesQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Invoices(request, from, to).CountAsync(cancellationToken);

    /// <summary>M10 E2: hóa đơn + dòng, rồi tra cứu theo id — bỏ nợ / ngày thu từ phân bổ còn hiệu lực, sửa giảm từ <c>collection_reductions</c>.</summary>
    public async Task<InvoiceExportData> LoadInvoicesAsync(ExportMonthlyInvoicesQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var invoices = await Invoices(request, from, to).AsNoTracking().Include(i => i.Lines).AsSplitQuery().ToListAsync(cancellationToken);
        var ids = invoices.Select(i => i.Id).ToList();
        var propertyIds = invoices.Select(i => i.PropertyId).Distinct().ToList();
        var roomIds = invoices.Select(i => i.RoomId).Distinct().ToList();
        var feeIds = invoices.SelectMany(i => i.Lines).Where(l => l.Type == InvoiceLineType.Metered && l.FeeTypeId != null)
            .Select(l => l.FeeTypeId!.Value).Distinct().ToList();

        var propertyNames = await db.Properties.Where(p => propertyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var roomCodes = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
        var systemCodes = await db.FeeTypes.Where(f => feeIds.Contains(f.Id) && f.SystemCode != null)
            .ToDictionaryAsync(f => f.Id, f => f.SystemCode!, cancellationToken);

        var allocations = await (
                from p in db.Payments
                where p.Status == PaymentStatus.Recorded && (p.Kind == PaymentKind.Receipt || p.Kind == PaymentKind.WriteOff)
                from a in p.Allocations
                where a.CancelledAt == null && ids.Contains(a.InvoiceId)
                select new { a.InvoiceId, a.Amount, p.Kind, p.Note, p.CreatedBy, p.PaidAt })
            .ToListAsync(cancellationToken);
        var writeOffs = allocations.Where(a => a.Kind == PaymentKind.WriteOff)
            .Select(a => new InvoiceMoneyNote(a.InvoiceId, a.Amount, a.Note, a.CreatedBy)).ToList();
        var lastPaidAt = allocations.Where(a => a.Kind == PaymentKind.Receipt).GroupBy(a => a.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.PaidAt));
        var reductions = await db.CollectionReductions.Where(r => ids.Contains(r.InvoiceId)).OrderBy(r => r.CreatedAt)
            .Select(r => new InvoiceMoneyNote(r.InvoiceId, r.AmountBefore - r.AmountAfter, r.Reason, r.CreatedBy))
            .ToListAsync(cancellationToken);

        var userIds = writeOffs.Concat(reductions).Where(n => n.UserId != null).Select(n => n.UserId!.Value).Distinct().ToList();
        var userNames = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return new InvoiceExportData(invoices, propertyNames, roomCodes, systemCodes, writeOffs, reductions, lastPaidAt, userNames);
    }

    private IQueryable<Invoice> Invoices(ExportMonthlyInvoicesQuery request, DateOnly from, DateOnly to)
    {
        var query = db.Invoices.Where(i => i.BillingMonth >= from && i.BillingMonth <= to
            && (i.Status == InvoiceStatus.Finalized || (request.IncludeDrafts && i.Status == InvoiceStatus.Draft)));
        if (request.PropertyIds is { Count: > 0 } propertyIds)
            query = query.Where(i => propertyIds.Contains(i.PropertyId));
        return query;
    }
}

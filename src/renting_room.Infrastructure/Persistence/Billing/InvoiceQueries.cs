using Microsoft.EntityFrameworkCore;
using renting_room.Application.Billing;
using renting_room.Application.Common.Models;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class InvoiceQueries(AppDbContext db) : IInvoiceQueries
{
    public Task<Invoice?> GetDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<Invoice?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<PagedResult<Invoice>> ListAsync(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = Filter(request);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(i => i.PeriodStart).ThenBy(i => i.SnapshotRoomCode)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Invoice>(items, request.Page, request.PageSize, total);
    }

    public async Task<InvoiceTotalsDto> GetTotalsAsync(ListInvoicesQuery filter, DateOnly today, CancellationToken cancellationToken)
    {
        var query = Filter(filter);
        var finalized = await query.Where(i => i.Status == InvoiceStatus.Finalized && i.TotalAmount > 0)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(), Total = g.Sum(i => i.TotalAmount), Paid = g.Sum(i => i.PaidAmount), WrittenOff = g.Sum(i => i.WrittenOffAmount),
                Outstanding = g.Sum(i => i.PaidAmount < i.TotalAmount ? i.TotalAmount - i.PaidAmount : 0),
                Overdue = g.Sum(i => i.PaidAmount < i.TotalAmount && i.DueDate < today ? i.TotalAmount - i.PaidAmount : 0)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var drafts = await query.Where(i => i.Status == InvoiceStatus.Draft).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(i => i.TotalAmount) }).FirstOrDefaultAsync(cancellationToken);
        var paid = finalized?.Paid ?? 0;
        var writtenOff = finalized?.WrittenOff ?? 0;
        return new InvoiceTotalsDto(finalized?.Count ?? 0, finalized?.Total ?? 0, paid, finalized?.Outstanding ?? 0,
            finalized?.Overdue ?? 0, drafts?.Count ?? 0, drafts?.Total ?? 0, paid - writtenOff, writtenOff);
    }

    private IQueryable<Invoice> Filter(ListInvoicesQuery request)
    {
        var query = db.Invoices.AsNoTracking();
        if (request.PropertyId is { } propertyId)
            query = query.Where(i => i.PropertyId == propertyId);
        if (BillingMonths.Parse(request.BillingMonth) is { } month)
            query = query.Where(i => i.BillingMonth == month);
        if (request.Status is { } status)
            query = query.Where(i => i.Status == status);
        if (request.RoomId is { } roomId)
            query = query.Where(i => i.RoomId == roomId);
        if (request.ContractId is { } contractId)
            query = query.Where(i => i.ContractId == contractId);
        if (!string.IsNullOrWhiteSpace(request.Floor))
            query = query.Where(i => db.Rooms.Any(r => r.Id == i.RoomId && r.Floor == request.Floor.Trim()));
        if (request.UnpaidOnly == true)
            query = query.Where(i => i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount);
        if (request.Stale == true)
            query = query.Where(i => i.Status == InvoiceStatus.Draft && i.IsStale);
        if (request.Adjusted is { } adjusted)
            query = query.Where(i => i.IsAdjusted == adjusted);
        if (request.NeedsResend is { } resend)
            query = query.Where(i => i.NeedsResend == resend);
        return query;
    }

    public async Task<decimal> GetContractOutstandingAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount), cancellationToken) ?? 0;

    public async Task<decimal> GetRefundDueAsync(Guid contractId, CancellationToken cancellationToken) =>
        -(await db.Invoices.Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Finalized && i.TotalAmount < 0 && i.RefundedOn == null)
            .SumAsync(i => (decimal?)i.TotalAmount, cancellationToken) ?? 0);

    public Task<decimal?> GetInvoiceOutstandingAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.Id == invoiceId && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .Select(i => (decimal?)(i.TotalAmount - i.PaidAmount)).FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetContractIdAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.Id == invoiceId).Select(i => (Guid?)i.ContractId).FirstOrDefaultAsync(cancellationToken);

    public Task<InvoiceStatus?> GetStatusAsync(Guid invoiceId, Guid contractId, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.Id == invoiceId && i.ContractId == contractId)
            .Select(i => (InvoiceStatus?)i.Status).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string?>> GetNumbersAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.InvoiceNo, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, InvoiceRef>> GetRefsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .Select(i => new InvoiceRef(i.Id, i.InvoiceNo, i.Status, i.BillingMonth)).ToDictionaryAsync(i => i.Id, cancellationToken);

    public async Task<IReadOnlyList<InvoiceBalanceRow>> ListForBalanceAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking().Where(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void)
            .OrderBy(i => i.PeriodStart)
            .Select(i => new InvoiceBalanceRow(i.Id, i.InvoiceNo, i.BillingMonth, i.Type, i.Status, i.TotalAmount, i.PaidAmount, i.WrittenOffAmount, i.DueDate,
                i.PeriodStart))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InvoicePeriodRow>> ListPeriodsAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking().Where(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void)
            .Select(i => new InvoicePeriodRow(i.Type, i.Status, i.PeriodStart, i.PeriodEnd)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PreviousDebtDto>> ListPreviousDebtsAsync(
        Guid contractId, Guid excludeInvoiceId, DateOnly periodStart, CancellationToken cancellationToken)
    {
        var debts = await db.Invoices.AsNoTracking()
            .Where(i => i.ContractId == contractId && i.Id != excludeInvoiceId && i.Status == InvoiceStatus.Finalized
                && i.PaidAmount < i.TotalAmount && i.PeriodStart < periodStart)
            .OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart)
            .Select(i => new { i.Id, i.InvoiceNo, i.BillingMonth, i.DueDate, Outstanding = i.TotalAmount - i.PaidAmount })
            .ToListAsync(cancellationToken);
        return debts.Select(d => new PreviousDebtDto(d.Id, d.InvoiceNo, $"{d.BillingMonth:yyyy-MM}", d.DueDate, d.Outstanding)).ToList();
    }

    public async Task<IReadOnlyList<ContractDebtRow>> ListOpenDebtsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking()
            .Where(i => contractIds.Contains(i.ContractId) && i.Status == InvoiceStatus.Finalized && i.PaidAmount < i.TotalAmount)
            .OrderBy(i => i.DueDate).ThenBy(i => i.PeriodStart)
            .Select(i => new ContractDebtRow(i.ContractId, i.Id, i.InvoiceNo, i.BillingMonth, i.DueDate, i.TotalAmount - i.PaidAmount))
            .ToListAsync(cancellationToken);

    public Task<bool> HasLaterInvoiceAsync(Invoice invoice, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.ContractId == invoice.ContractId && i.Status != InvoiceStatus.Void && i.Id != invoice.Id
            && (i.PeriodStart > invoice.PeriodStart
                || (invoice.Type == InvoiceType.Regular && i.Type == InvoiceType.Final && i.PeriodStart == invoice.PeriodStart)), cancellationToken);

    public Task<bool> HasFinalizedFinalAsync(Guid contractId, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.ContractId == contractId && i.Type == InvoiceType.Final && i.Status == InvoiceStatus.Finalized, cancellationToken);

    public Task<bool> HasDraftAsync(Guid contractId, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void && i.Status == InvoiceStatus.Draft, cancellationToken);

    public Task<bool> HasInvoiceAfterAsync(Guid contractId, DateOnly date, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void && i.PeriodStart > date, cancellationToken);

    public Task<bool> HasInvoiceForRoomFromAsync(Guid contractId, Guid roomId, DateOnly date, CancellationToken cancellationToken) =>
        db.Invoices.AnyAsync(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void && i.RoomId == roomId
            && i.PeriodEnd >= date, cancellationToken);

    public Task<List<Guid>> ListSegmentEndReadingIdsAsync(Guid contractId, IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Segments).Where(s => !s.Voided && meterIds.Contains(s.MeterId)).Select(s => s.EndReadingId).ToListAsync(cancellationToken);

    public Task<Guid?> FindDraftIdForChargeAsync(Guid contractId, DateOnly incurredOn, CancellationToken cancellationToken) =>
        db.Invoices
            .Where(i => i.ContractId == contractId && i.Status == InvoiceStatus.Draft && (i.Type == InvoiceType.Final || i.PeriodEnd >= incurredOn))
            .OrderBy(i => i.PeriodStart).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<decimal> GetRefundLinesTotalAsync(Guid sourceInvoiceId, Guid? excludeLineId, CancellationToken cancellationToken) =>
        await db.Invoices.Where(i => i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Lines).Where(l => l.SourceInvoiceId == sourceInvoiceId && l.Type == InvoiceLineType.Refund && l.Id != excludeLineId)
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0;

    public async Task<IReadOnlyList<InvoiceTarget>> ListRecalculationTargetsAsync(RecalculateInvoicesCommand request, CancellationToken cancellationToken)
    {
        var query = db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Void);
        if (request.InvoiceIds is { Count: > 0 } ids)
            query = query.Where(i => ids.Contains(i.Id));
        else
        {
            query = query.Where(i => i.PropertyId == request.PropertyId);
            if (BillingMonths.Parse(request.BillingMonth) is { } month)
                query = query.Where(i => i.BillingMonth == month);
            if (request.RoomIds is { Count: > 0 } roomIds)
                query = query.Where(i => roomIds.Contains(i.RoomId));
            if (!string.IsNullOrWhiteSpace(request.Floor))
                query = query.Where(i => db.Rooms.Any(r => r.Id == i.RoomId && r.Floor == request.Floor.Trim()));
        }
        if (request.StaleOnly)
            query = query.Where(i => i.Status == InvoiceStatus.Draft && i.IsStale);
        return await query.Select(i => new InvoiceTarget(i.Id, i.ContractId, i.RoomId, i.SnapshotRoomCode, i.Status)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceTarget>> ListManualLineTargetsAsync(BulkAddManualLineCommand request, CancellationToken cancellationToken)
    {
        var query = db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Void);
        if (request.InvoiceIds is { Count: > 0 } ids)
            query = query.Where(i => ids.Contains(i.Id));
        else
        {
            var month = BillingMonths.Parse(request.BillingMonth)!.Value;
            query = query.Where(i => i.PropertyId == request.PropertyId && i.BillingMonth == month);
            if (request.RoomIds is { Count: > 0 } roomIds)
                query = query.Where(i => roomIds.Contains(i.RoomId));
            if (!string.IsNullOrWhiteSpace(request.Floor))
                query = query.Where(i => db.Rooms.Any(r => r.Id == i.RoomId && r.Floor == request.Floor.Trim()));
        }
        return await query.Select(i => new InvoiceTarget(i.Id, i.ContractId, i.RoomId, i.SnapshotRoomCode, i.Status)).ToListAsync(cancellationToken);
    }

    public async Task<InvoiceSourceData> LoadSourceDataAsync(
        Contract contract, DateOnly periodStart, Guid? excludeInvoiceId, bool isFinal, CancellationToken cancellationToken)
    {
        var roomIds = contract.RoomStays.Select(s => s.RoomId).Distinct().ToList();
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => roomIds.Contains(m.RoomId)).ToListAsync(cancellationToken);
        var others = db.Invoices.AsNoTracking()
            .Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void && (excludeInvoiceId == null || i.Id != excludeInvoiceId));
        var previous = (await others.Where(i => isFinal ? i.PeriodStart <= periodStart : i.PeriodStart < periodStart)
                .SelectMany(i => i.Segments.Select(s => new { s.MeterId, s.EndReadingId, i.PeriodStart }))
                .ToListAsync(cancellationToken))
            .Select(s => (s.MeterId, s.EndReadingId, s.PeriodStart)).ToList();

        List<decimal>? rentLines = null;
        var hasRegular = false;
        if (isFinal)
        {
            rentLines = await others.SelectMany(i => i.Lines)
                .Where(l => l.Type == InvoiceLineType.Rent && l.ServiceFrom <= periodStart && l.ServiceTo >= periodStart)
                .Select(l => l.Amount).ToListAsync(cancellationToken);
            hasRegular = await others.AnyAsync(i => i.Type == InvoiceType.Regular && i.PeriodStart == periodStart, cancellationToken);
        }
        var recent = (await others.Where(i => i.Type == InvoiceType.Regular && i.PeriodStart < periodStart)
                .SelectMany(i => i.Lines.Where(l => l.Type == InvoiceLineType.Metered && l.FeeTypeId != null)
                    .Select(l => new { FeeTypeId = l.FeeTypeId!.Value, i.PeriodStart, l.Quantity }))
                .ToListAsync(cancellationToken))
            .Select(x => (x.FeeTypeId, x.PeriodStart, x.Quantity)).ToList();
        var closed = (await db.RoomCharges.AsNoTracking()
                .Where(c => c.ContractId == contract.Id && c.CancelledAt == null && c.EndReadingId != null)
                .Select(c => c.EndReadingId!.Value).ToListAsync(cancellationToken))
            .ToHashSet();
        var roomCodes = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
        return new InvoiceSourceData(meters, previous, rentLines, hasRegular, recent, closed, roomCodes);
    }

    public async Task<HashSet<Guid>> GetLockedReadingIdsAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken)
    {
        var segments = await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Segments)
            .Where(s => meterIds.Contains(s.MeterId) && !s.Voided)
            .Select(s => new { s.StartReadingId, s.EndReadingId })
            .ToListAsync(cancellationToken);
        var closings = await db.RoomCharges.Where(c => c.MeterId != null && meterIds.Contains(c.MeterId.Value) && c.CancelledAt == null)
            .Select(c => new { c.StartReadingId, c.EndReadingId })
            .ToListAsync(cancellationToken);
        return segments.SelectMany(s => new[] { s.StartReadingId, s.EndReadingId })
            .Concat(closings.SelectMany(c => new[] { c.StartReadingId!.Value, c.EndReadingId!.Value }))
            .ToHashSet();
    }

    public async Task<Dictionary<Guid, DateOnly>> GetMeterLockedUntilAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken)
    {
        var segments = await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Segments.Where(s => meterIds.Contains(s.MeterId) && !s.Voided).Select(s => new { s.MeterId, s.FeeTypeId, InvoiceId = i.Id }))
            .ToListAsync(cancellationToken);
        if (segments.Count == 0)
            return [];
        var invoiceIds = segments.Select(s => s.InvoiceId).Distinct().ToList();
        var usageEnds = (await db.Invoices.Where(i => invoiceIds.Contains(i.Id))
                .SelectMany(i => i.Lines.Where(l => l.Type == InvoiceLineType.Metered && l.FeeTypeId != null)
                    .Select(l => new { InvoiceId = i.Id, FeeTypeId = l.FeeTypeId!.Value, l.ServiceTo }))
                .ToListAsync(cancellationToken))
            .GroupBy(l => (l.InvoiceId, l.FeeTypeId))
            .ToDictionary(g => g.Key, g => g.Max(l => l.ServiceTo));
        return segments
            .Where(s => usageEnds.ContainsKey((s.InvoiceId, s.FeeTypeId)))
            .GroupBy(s => s.MeterId)
            .ToDictionary(g => g.Key, g => g.Max(s => usageEnds[(s.InvoiceId, s.FeeTypeId)]));
    }

    public async Task<SegmentRef?> FindFinalizedSegmentEndingAtAsync(Guid readingId, CancellationToken cancellationToken)
    {
        var segment = await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Segments.Where(s => s.EndReadingId == readingId && !s.Voided)
                .Select(s => new { InvoiceId = i.Id, i.ContractId, s.FeeTypeId, s.UsageStart }))
            .FirstOrDefaultAsync(cancellationToken);
        return segment is null ? null : new SegmentRef(segment.InvoiceId, segment.ContractId, segment.FeeTypeId, segment.UsageStart);
    }

    public Task<bool> HasFinalizedSegmentStartingAtAsync(Guid readingId, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .AnyAsync(i => i.Segments.Any(s => s.StartReadingId == readingId && !s.Voided), cancellationToken);

    public Task<InvoiceLine> GetSystemMeteredLineAsync(Guid invoiceId, Guid feeTypeId, DateOnly? usageStart, CancellationToken cancellationToken) =>
        db.Invoices.Where(i => i.Id == invoiceId).SelectMany(i => i.Lines)
            .Where(l => l.IsSystem && l.Type == InvoiceLineType.Metered && l.FeeTypeId == feeTypeId
                && (usageStart == null || l.ServiceFrom == usageStart))
            .FirstAsync(cancellationToken);

    public async Task<IReadOnlyList<DraftRef>> ListDraftsUsingMetersAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken)
    {
        var roomIds = db.Meters.Where(m => meterIds.Contains(m.Id)).Select(m => m.RoomId);
        return await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Draft
                && (roomIds.Contains(i.RoomId) || i.Segments.Any(s => meterIds.Contains(s.MeterId) && !s.Voided)))
            .Select(i => new DraftRef(i.Id, i.ContractId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SheetInvoiceRow>> ListSheetInvoicesAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking()
            .Where(i => contractIds.Contains(i.ContractId) && i.Status != InvoiceStatus.Void && i.Type == InvoiceType.Regular)
            .Select(i => new SheetInvoiceRow(i.Id, i.ContractId, i.PeriodStart, i.Status, i.IsStale))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InvoiceAdjustment>> ListAdjustmentsAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.InvoiceAdjustments.AsNoTracking().Where(a => a.InvoiceId == invoiceId)
            .OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SegmentEndRow>> ListSegmentEndsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        (await db.Invoices.AsNoTracking()
            .Where(i => contractIds.Contains(i.ContractId) && i.Status != InvoiceStatus.Void)
            .SelectMany(i => i.Segments.Select(s => new { i.ContractId, i.PeriodStart, s.MeterId, s.EndReadingId }))
            .ToListAsync(cancellationToken))
        .Select(s => new SegmentEndRow(s.ContractId, s.PeriodStart, s.MeterId, s.EndReadingId)).ToList();

    public async Task<IReadOnlyList<MeteredUsageRow>> ListMeteredUsageAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken) =>
        (await db.Invoices.AsNoTracking()
            .Where(i => contractIds.Contains(i.ContractId) && i.Status != InvoiceStatus.Void && i.Type == InvoiceType.Regular)
            .SelectMany(i => i.Lines.Where(l => l.Type == InvoiceLineType.Metered && l.FeeTypeId != null)
                .Select(l => new { i.ContractId, FeeTypeId = l.FeeTypeId!.Value, i.PeriodStart, l.Quantity }))
            .ToListAsync(cancellationToken))
        .Select(x => new MeteredUsageRow(x.ContractId, x.FeeTypeId, x.PeriodStart, x.Quantity)).ToList();
}

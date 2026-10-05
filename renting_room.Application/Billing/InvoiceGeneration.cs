using System.Globalization;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

internal static class BillingMonths
{
    /// <summary>"yyyy-MM" → ngày 1 của tháng.</summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ? month : null;

    public static IRuleBuilderOptions<T, string?> BillingMonth<T>(this IRuleBuilder<T, string?> rule, TimeProvider clock) =>
        rule.Must(v => Parse(v) is { } m && m <= clock.GetUtcNow().ToBusinessDate().AddMonths(1))
            .WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng thu dạng yyyy-MM, tối đa tháng sau.");

    /// <summary>Kỳ của HĐ có ngày bắt đầu thuộc tháng thu (C-05: tối đa 1).</summary>
    public static BillingPeriod? PeriodIn(Contract contract, DateOnly month) =>
        contract.BillingPeriods(month.AddMonths(1).AddDays(-1)).FirstOrDefault(p => p.Start.Year == month.Year && p.Start.Month == month.Month);
}

internal static class InvoiceInputs
{
    /// <summary>
    /// Dữ liệu nguồn cho <see cref="InvoiceCalculator"/>: dịch vụ + điện nước có công tơ ở phòng (kèm giá), công tơ của phòng (kèm chỉ số),
    /// chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy kỳ trước (chuỗi liên tục MT-BR-12).
    /// </summary>
    /// <param name="type"><c>Final</c> ⇒ kèm bối cảnh quyết toán (tiền phòng kỳ cuối đã thu chưa, kỳ cuối đã có phiếu thường chưa) và
    /// nối chuỗi chỉ số cả với phiếu thường của chính kỳ cuối.</param>
    public static async Task<InvoiceCalcInput> LoadAsync(
        IAppDbContext db, Contract contract, BillingPeriod period, Guid? excludeInvoiceId, CancellationToken ct,
        InvoiceType type = InvoiceType.Regular)
    {
        var isFinal = type == InvoiceType.Final;
        var types = await ContractFeeRules.LoadTypesAsync(db, contract, ct);
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => m.RoomId == contract.RoomId).ToListAsync(ct);
        var others = db.Invoices.AsNoTracking()
            .Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void && (excludeInvoiceId == null || i.Id != excludeInvoiceId));
        var previous = await others.Where(i => isFinal ? i.PeriodStart <= period.Start : i.PeriodStart < period.Start)
            .SelectMany(i => i.Segments.Select(s => new { s.MeterId, s.EndReadingId, i.PeriodStart }))
            .ToListAsync(ct);
        var lastEnd = previous.GroupBy(s => s.MeterId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.PeriodStart)!.EndReadingId);

        FinalSettlement? final = null;
        if (isFinal)
        {
            var rentCovered = await others.SelectMany(i => i.Lines)
                .AnyAsync(l => l.Type == InvoiceLineType.Rent && l.ServiceFrom <= period.Start && l.ServiceTo >= period.Start, ct);
            var hasRegular = await others.AnyAsync(i => i.Type == InvoiceType.Regular && i.PeriodStart == period.Start, ct);
            final = new FinalSettlement(rentCovered, hasRegular);
        }
        return new InvoiceCalcInput(contract, period, types, meters, lastEnd, final);
    }

    /// <summary>Kỳ hiện tại của phiếu theo HĐ (có thể bị cắt ngắn nếu HĐ đã bắt đầu thanh lý).</summary>
    public static BillingPeriod? CurrentPeriod(Contract contract, Invoice invoice) =>
        contract.BillingPeriods(invoice.PeriodStart).FirstOrDefault(p => p.Start == invoice.PeriodStart);
}

// ============================================================ Tạo phiếu nháp hàng loạt

public sealed record SkippedContract(Guid ContractId, string ContractNo, string RoomCode, string Reason);

public sealed record GenerateInvoicesResult(int Created, int Recalculated, int WithIssues, IReadOnlyList<SkippedContract> Skipped);

/// <param name="RoomIds">Lọc phòng (null = cả khu).</param>
/// <param name="RecalculateExistingDrafts">Kỳ đã có phiếu nháp ⇒ tính lại (giữ ô sửa tay) thay vì bỏ qua.</param>
public sealed record GenerateInvoicesCommand(
    Guid PropertyId, string BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool RecalculateExistingDrafts)
    : IRequest<Result<GenerateInvoicesResult>>;

public sealed class GenerateInvoicesCommandValidator : AbstractValidator<GenerateInvoicesCommand>
{
    public GenerateInvoicesCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.BillingMonth).BillingMonth(clock);
        RuleFor(x => x.RoomIds).Must(r => r is null || r.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
    }
}

/// <summary>
/// BL-UC-01: tạo phiếu nháp kỳ có ngày bắt đầu thuộc tháng thu cho từng HĐ của khu — mỗi HĐ 1 transaction, khóa HĐ (C-07).
/// Bỏ qua kèm lý do: đã có phiếu, đã lập kỳ sau, chưa lập kỳ trước (BL-BR-21 — trừ phiếu đầu tiên của HĐ), không có kỳ trong tháng.
/// </summary>
public sealed class GenerateInvoicesHandler(IAppDbContext db) : IRequestHandler<GenerateInvoicesCommand, Result<GenerateInvoicesResult>>
{
    public async ValueTask<Result<GenerateInvoicesResult>> Handle(GenerateInvoicesCommand request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var month = BillingMonths.Parse(request.BillingMonth)!.Value;
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var rooms = db.Rooms.Where(r => r.PropertyId == request.PropertyId);
        if (request.RoomIds is { Count: > 0 } roomIds)
            rooms = rooms.Where(r => roomIds.Contains(r.Id));
        if (!string.IsNullOrWhiteSpace(request.Floor))
            rooms = rooms.Where(r => r.Floor == request.Floor.Trim());
        var roomCodes = await rooms.ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
        var roomIdList = roomCodes.Keys.ToList();

        var candidates = await db.Contracts.AsNoTracking()
            .Where(c => roomIdList.Contains(c.RoomId)
                && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.StartDate <= monthEnd && (c.ActualEndDate == null || c.ActualEndDate >= month))
            .OrderBy(c => c.ContractNo)
            .Select(c => new { c.Id, c.RoomId, c.ContractNo })
            .ToListAsync(cancellationToken);

        int created = 0, recalculated = 0, withIssues = 0;
        var skipped = new List<SkippedContract>();
        foreach (var candidate in candidates)
        {
            var outcome = await GenerateOneAsync(candidate.Id, roomCodes[candidate.RoomId], month, request.RecalculateExistingDrafts, cancellationToken);
            switch (outcome.Result)
            {
                case "CREATED": created++; break;
                case "RECALCULATED": recalculated++; break;
                default: skipped.Add(new SkippedContract(candidate.Id, candidate.ContractNo, roomCodes[candidate.RoomId], outcome.Result)); break;
            }
            if (outcome.HasIssues)
                withIssues++;
        }
        return new GenerateInvoicesResult(created, recalculated, withIssues, skipped);
    }

    private async Task<(string Result, bool HasIssues)> GenerateOneAsync(
        Guid contractId, string roomCode, DateOnly month, bool recalculateDrafts, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId, ct))!;
        var period = BillingMonths.PeriodIn(contract, month);
        if (period is null)
            return ("NO_PERIOD_IN_MONTH", false);
        // BL-BR-02: kỳ chứa ngày trả phòng do phiếu quyết toán đảm nhận.
        if (contract.ActualEndDate is { } actualEnd && actualEnd <= period.End)
            return ("USE_FINAL_INVOICE", false);

        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery()
            .Where(i => i.ContractId == contractId && i.Status != InvoiceStatus.Void).ToListAsync(ct);
        var existing = invoices.FirstOrDefault(i => i.PeriodStart == period.Start);
        if (existing is not null && (existing.Status != InvoiceStatus.Draft || !recalculateDrafts))
            return ("EXISTS", false);
        if (existing is null && invoices.Any(i => i.PeriodStart > period.Start))
            return ("LATER_PERIOD_BILLED", false);
        if (existing is null && invoices.Count > 0 && invoices.All(i => i.PeriodEnd != period.Start.AddDays(-1)))
            return ("PREVIOUS_PERIOD_NOT_BILLED", false);

        var calculation = InvoiceCalculator.Calculate(await InvoiceInputs.LoadAsync(db, contract, period, existing?.Id, ct));
        Invoice invoice;
        if (existing is not null)
        {
            existing.ApplyCalculation(calculation, keepManualEdits: true, period.End);
            invoice = existing;
        }
        else
        {
            var representative = await db.Renters.Where(r => r.Id == contract.RepresentativeRenterId).Select(r => r.FullName).FirstAsync(ct);
            invoice = Invoice.CreateDraft(contract.PropertyId, contract.RoomId, contract.Id, period.Start, period.End,
                roomCode, contract.ContractNo, representative, calculation);
            db.Invoices.Add(invoice);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (existing is null ? "CREATED" : "RECALCULATED", invoice.HasBlockingIssues);
    }
}

// ============================================================ Tính lại nháp theo phạm vi

public sealed record RecalculateInvoicesResult(int Recalculated, int WithIssues, IReadOnlyList<Guid> NotDraft);

/// <summary>BL-UC-06: phạm vi = danh sách phiếu, hoặc khu + tháng (lọc phòng / tầng). Mặc định giữ ô sửa tay.</summary>
public sealed record RecalculateInvoicesCommand(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    bool KeepManualEdits = true) : IRequest<Result<RecalculateInvoicesResult>>;

public sealed class RecalculateInvoicesCommandValidator : AbstractValidator<RecalculateInvoicesCommand>
{
    public RecalculateInvoicesCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x).Must(x => x.InvoiceIds is { Count: > 0 } || (x.PropertyId is not null && x.BillingMonth is not null))
            .WithErrorCode("SCOPE_REQUIRED").WithMessage("Chọn phiếu, hoặc khu + tháng thu.");
        RuleFor(x => x.BillingMonth).BillingMonth(clock).When(x => x.BillingMonth is not null);
        RuleFor(x => x.InvoiceIds).Must(i => i is null || i.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
    }
}

public sealed class RecalculateInvoicesHandler(IAppDbContext db) : IRequestHandler<RecalculateInvoicesCommand, Result<RecalculateInvoicesResult>>
{
    public async ValueTask<Result<RecalculateInvoicesResult>> Handle(RecalculateInvoicesCommand request, CancellationToken cancellationToken)
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
        var targets = await query.Select(i => new { i.Id, i.ContractId, i.Status }).ToListAsync(cancellationToken);

        int recalculated = 0, withIssues = 0;
        foreach (var target in targets.Where(t => t.Status == InvoiceStatus.Draft))
        {
            var hasIssues = await RecalculateOneAsync(target.ContractId, target.Id, request.KeepManualEdits, cancellationToken);
            recalculated++;
            if (hasIssues)
                withIssues++;
        }
        return new RecalculateInvoicesResult(recalculated, withIssues,
            targets.Where(t => t.Status != InvoiceStatus.Draft).Select(t => t.Id).ToList());
    }

    private async Task<bool> RecalculateOneAsync(Guid contractId, Guid invoiceId, bool keepManualEdits, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId, ct);
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId, ct))!;
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery().FirstAsync(i => i.Id == invoiceId, ct);
        if (invoice.Status != InvoiceStatus.Draft)
            return false;

        var period = InvoiceInputs.CurrentPeriod(contract, invoice) ?? new BillingPeriod(invoice.PeriodStart, invoice.PeriodEnd);
        var calculation = InvoiceCalculator.Calculate(await InvoiceInputs.LoadAsync(db, contract, period, invoice.Id, ct, invoice.Type));
        invoice.ApplyCalculation(calculation, keepManualEdits, period.End);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.HasBlockingIssues;
    }
}

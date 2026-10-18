using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

/// <param name="RoomIds">Lọc phòng (null = cả khu).</param>
/// <param name="RecalculateExistingDrafts">Kỳ đã có phiếu nháp ⇒ tính lại (giữ ô sửa tay) thay vì bỏ qua.</param>
/// <param name="ContractId">Chỉ lập cho 1 HĐ cụ thể của khu (BL-UC-01).</param>
public sealed record GenerateInvoicesCommand(
    Guid PropertyId, string BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool RecalculateExistingDrafts, Guid? ContractId = null)
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
/// BL-UC-01: tạo phiếu nháp kỳ có tháng thu = tháng chọn cho từng HĐ của khu — mỗi HĐ 1 transaction, khóa HĐ (C-07).
/// Bỏ qua kèm lý do: đã có phiếu, đã lập kỳ sau, chưa lập kỳ trước (BL-BR-21 — kỳ đầu = kỳ chứa "Tính tiền từ ngày"), không có kỳ trong tháng,
/// thiếu chỉ số (nháp chỉ lập khi đủ chỉ số), kỳ trước còn nháp thiếu chỉ số.
/// </summary>
public sealed class GenerateInvoicesHandler(IAppDbContext db) : IRequestHandler<GenerateInvoicesCommand, Result<GenerateInvoicesResult>>
{
    private const string MissingReadingCode = "MISSING_READING";

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
                && c.BillingStartDate <= monthEnd.AddMonths(1) && (c.ActualEndDate == null || c.ActualEndDate >= month))
            .Where(c => request.ContractId == null || c.Id == request.ContractId)
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
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId, ct))!;
        var schedule = (await PropertyBilling.LoadAsync(db, contract.PropertyId, ct)).Schedule;
        var period = BillingMonths.PeriodIn(contract, month, schedule);
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
        // BL-BR-21: lập lần lượt từ kỳ đầu (kỳ chứa "Tính tiền từ ngày") — không để kỳ sau nuốt / bỏ sót kỳ trước.
        if (existing is null && period.Start != contract.BillingStartDate && invoices.All(i => i.PeriodEnd != period.Start.AddDays(-1)))
            return ("PREVIOUS_PERIOD_NOT_BILLED", false);
        // BL-BR-21 (17/10/2026): nháp kỳ trước mất chỉ số sau khi lập ⇒ không lập kỳ sau, tránh kỳ sau gộp sản lượng của kỳ trước.
        if (existing is null && invoices.Any(i => i.Status == InvoiceStatus.Draft && i.PeriodStart < period.Start
                && i.Issues.Any(x => x.Code == MissingReadingCode)))
            return ("PREVIOUS_PERIOD_INCOMPLETE", false);

        var calculation = InvoiceCalculator.Calculate(await InvoiceInputs.LoadAsync(db, contract, period, existing?.Id, ct));
        // Nháp chỉ lập khi đủ chỉ số; nháp đã có thì vẫn tính lại (lỗi hiện trên nháp, chặn chốt).
        if (existing is null && calculation.Issues.Any(x => x.Code == MissingReadingCode))
            return (MissingReadingCode, false);
        Invoice invoice;
        if (existing is not null)
        {
            existing.ApplyCalculation(calculation, keepManualEdits: true, period.End);
            invoice = existing;
        }
        else
        {
            var representative = await db.Renters.Where(r => r.Id == contract.RepresentativeRenterId).Select(r => r.FullName).FirstAsync(ct);
            invoice = Invoice.CreateDraft(contract.PropertyId, contract.RoomId, contract.Id, period,
                roomCode, contract.ContractNo, representative, calculation);
            db.Invoices.Add(invoice);
        }
        // BL-BR-32/36: khoản phát sinh đang chờ của HĐ tự vào nháp.
        await RoomChargeBilling.AttachPendingAsync(db, invoice, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (existing is null ? "CREATED" : "RECALCULATED", invoice.HasBlockingIssues);
    }
}

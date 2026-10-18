using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record RecalculateInvoicesResult(int Recalculated, int WithIssues, IReadOnlyList<Guid> NotDraft);

/// <summary>BL-UC-06: phạm vi = danh sách phiếu, hoặc khu + tháng (lọc phòng / tầng). Mặc định giữ ô sửa tay.</summary>
/// <param name="StaleOnly">BL-BR-20 (I1): chỉ các nháp "Cần tính lại" — theo khu, tháng thu tùy chọn.</param>
public sealed record RecalculateInvoicesCommand(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    bool KeepManualEdits = true, bool StaleOnly = false) : IRequest<Result<RecalculateInvoicesResult>>;

public sealed class RecalculateInvoicesCommandValidator : AbstractValidator<RecalculateInvoicesCommand>
{
    public RecalculateInvoicesCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x).Must(x => x.InvoiceIds is { Count: > 0 } || (x.PropertyId is not null && (x.BillingMonth is not null || x.StaleOnly)))
            .WithErrorCode("SCOPE_REQUIRED").WithMessage("Chọn phiếu, hoặc khu + tháng thu (tính lại nháp cần tính lại: chỉ cần khu).");
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
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId, ct);
        await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId, ct))!;
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Segments).AsSplitQuery().FirstAsync(i => i.Id == invoiceId, ct);
        if (invoice.Status != InvoiceStatus.Draft)
            return false;

        var schedule = (await PropertyBilling.LoadAsync(db, contract.PropertyId, ct)).Schedule;
        var period = InvoiceInputs.CurrentPeriod(contract, invoice, schedule)
            ?? new BillingPeriod(invoice.PeriodStart, invoice.PeriodEnd, invoice.BillingMonth);
        var calculation = InvoiceCalculator.Calculate(await InvoiceInputs.LoadAsync(db, contract, period, invoice.Id, ct, invoice.Type));
        invoice.ApplyCalculation(calculation, keepManualEdits, period.End);
        await RoomChargeBilling.AttachPendingAsync(db, invoice, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.HasBlockingIssues;
    }
}

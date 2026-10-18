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

public sealed record FinalizeInvoiceCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

/// <summary>
/// BL-BR-11/12/13: khóa HĐ → phiếu (C-07); tính lại phần hệ thống và so với nháp (bỏ qua ô sửa tay) — lệch ⇒ 409 DRAFT_STALE;
/// còn vấn đề chặn ⇒ 422; cấp số PB, hạn thanh toán = hôm nay + số ngày của HĐ.
/// </summary>
public sealed class FinalizeInvoiceHandler(IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<FinalizeInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(FinalizeInvoiceCommand request, CancellationToken cancellationToken) =>
        await FinalizeAsync(db, numbers, currentUser, clock, request.Id, cancellationToken);

    internal static async Task<Result<InvoiceDetailDto>> FinalizeAsync(
        IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock, Guid id, CancellationToken ct)
    {
        var contractId = await db.Invoices.Where(i => i.Id == id).Select(i => (Guid?)i.ContractId).FirstOrDefaultAsync(ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        await db.LockForUpdateAsync<Invoice>(id, ct);
        var contract = (await ContractMutation.LoadAsync(db, contractId.Value, ct))!;
        var invoice = (await InvoiceAccess.LoadAsync(db, id, ct))!;
        if (invoice.Status != InvoiceStatus.Draft)
            return BillingErrors.NotDraft;

        var billing = await PropertyBilling.LoadAsync(db, contract.PropertyId, ct);
        var period = InvoiceInputs.CurrentPeriod(contract, invoice, billing.Schedule);
        if (period is null || period.End != invoice.PeriodEnd)
            return BillingErrors.DraftStale;
        var calculation = InvoiceCalculator.Calculate(await InvoiceInputs.LoadAsync(db, contract, period, invoice.Id, ct, invoice.Type));
        if (!invoice.SystemPartMatches(calculation))
            return BillingErrors.DraftStale;

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        if (invoice.HasBlockingIssues)
            return BillingErrors.HasIssues(invoice.Issues.Where(i => i.Severity == InvoiceIssue.Error).Select(i => i.Code).Distinct().ToList());
        var refunds = await RefundSource.CheckInvoiceAsync(db, invoice, ct);
        if (refunds.IsFailure)
            return refunds.Error!;
        var invoiceNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PB", today.Year, ct);
        var finalized = invoice.Finalize(invoiceNo, today, billing.PaymentDueDays, now);
        if (finalized.IsFailure)
            return finalized.Error!;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}

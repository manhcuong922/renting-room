using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record FinalizeInvoiceCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class FinalizeInvoiceHandler(InvoiceFinalizer finalizer) : IRequestHandler<FinalizeInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(FinalizeInvoiceCommand request, CancellationToken cancellationToken) =>
        await finalizer.FinalizeAsync(request.Id, cancellationToken);
}

/// <summary>
/// BL-BR-11/12/13: khóa HĐ → phiếu (C-07); tính lại phần hệ thống và so với nháp (bỏ qua ô sửa tay) — lệch ⇒ 409 DRAFT_STALE;
/// còn vấn đề chặn ⇒ 422; cấp số PB, hạn thanh toán = hôm nay + số ngày của HĐ.
/// </summary>
public sealed class InvoiceFinalizer(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IContractRepository contracts,
    IPropertyQueries properties, InvoiceInputLoader inputs, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<InvoiceDetailDto>> FinalizeAsync(Guid id, CancellationToken ct)
    {
        var contractId = await invoiceQueries.GetContractIdAsync(id, ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, ct);
        await unitOfWork.LockForUpdateAsync<Invoice>(id, ct);
        var contract = (await contracts.GetForUpdateAsync(contractId.Value, ct))!;
        var invoice = (await invoices.GetForUpdateAsync(id, ct))!;
        if (invoice.Status != InvoiceStatus.Draft)
            return BillingErrors.NotDraft;

        var billing = await properties.GetBillingAsync(contract.PropertyId, ct);
        var period = InvoiceInputLoader.CurrentPeriod(contract, invoice, billing.Schedule);
        if (period is null || period.End != invoice.PeriodEnd)
            return BillingErrors.DraftStale;
        var calculation = InvoiceCalculator.Calculate(await inputs.LoadAsync(contract, period, invoice.Id, ct, invoice.Type));
        if (!invoice.SystemPartMatches(calculation))
            return BillingErrors.DraftStale;

        var now = clock.GetUtcNow();
        var today = now.ToBusinessDate();
        if (invoice.HasBlockingIssues)
            return BillingErrors.HasIssues(invoice.Issues.Where(i => i.Severity == InvoiceIssue.Error).Select(i => i.Code).Distinct().ToList());
        var refunds = await RefundSource.CheckInvoiceAsync(invoiceQueries, invoice, ct);
        if (refunds.IsFailure)
            return refunds.Error!;
        var invoiceNo = await numbers.NextAsync(currentUser.OrganizationId!.Value, "PB", today.Year, ct);
        var finalized = invoice.Finalize(invoiceNo, today, billing.PaymentDueDays, now);
        if (finalized.IsFailure)
            return finalized.Error!;

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.ToDetail(today);
    }
}

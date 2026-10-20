using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

public sealed record FinalizeInvoiceCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

public sealed class FinalizeInvoiceHandler(InvoiceFinalizer finalizer) : IRequestHandler<FinalizeInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(FinalizeInvoiceCommand request, CancellationToken cancellationToken) =>
        await finalizer.FinalizeAsync(request.Id, cancellationToken);
}

public sealed class InvoiceFinalizer(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IContractRepository contracts, IMeterQueries meters,
    IPropertyQueries properties, InvoiceInputLoader inputs, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<InvoiceDetailDto>> FinalizeAsync(Guid id, CancellationToken ct)
    {
        var contractId = await invoiceQueries.GetContractIdAsync(id, ct);
        if (contractId is null)
            return BillingErrors.NotFound;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, ct);
        var contract = (await contracts.GetForUpdateAsync(contractId.Value, ct))!;
        // Sửa chỉ số cùng phòng khóa HĐ → công tơ → nháp: chờ lệnh chốt xong, không lọt giữa lúc tính lại và lúc lưu (M06 §10).
        var roomIds = contract.RoomStays.Select(s => s.RoomId).Distinct().ToList();
        foreach (var meterId in (await meters.ListRoomMetersAsync(roomIds, ct)).Select(m => m.Id).Order())
            await unitOfWork.LockForUpdateAsync<Meter>(meterId, ct);
        await unitOfWork.LockForUpdateAsync<Invoice>(id, ct);
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

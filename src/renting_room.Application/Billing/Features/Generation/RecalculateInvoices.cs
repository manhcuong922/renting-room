using FluentValidation;
using Mediator;
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

public sealed class RecalculateInvoicesHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IPropertyQueries properties,
    InvoiceInputLoader inputs, RoomChargeBilling roomCharges)
    : IRequestHandler<RecalculateInvoicesCommand, Result<RecalculateInvoicesResult>>
{
    public async ValueTask<Result<RecalculateInvoicesResult>> Handle(RecalculateInvoicesCommand request, CancellationToken cancellationToken)
    {
        var targets = await invoiceQueries.ListRecalculationTargetsAsync(request, cancellationToken);

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
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId, ct);
        await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var contract = (await contracts.GetForUpdateAsync(contractId, ct))!;
        var invoice = (await invoices.GetForUpdateAsync(invoiceId, ct))!;
        if (invoice.Status != InvoiceStatus.Draft)
            return false;

        var schedule = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule;
        var period = InvoiceInputLoader.CurrentPeriod(contract, invoice, schedule)
            ?? new BillingPeriod(invoice.PeriodStart, invoice.PeriodEnd, invoice.BillingMonth);
        var calculation = InvoiceCalculator.Calculate(await inputs.LoadAsync(contract, period, invoice.Id, ct, invoice.Type));
        invoice.ApplyCalculation(calculation, keepManualEdits, period.End);
        await roomCharges.AttachPendingAsync(invoice, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invoice.HasBlockingIssues;
    }
}

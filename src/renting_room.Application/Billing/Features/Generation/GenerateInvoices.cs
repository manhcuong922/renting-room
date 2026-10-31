using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

public sealed record GenerateInvoicesCommand(
    Guid PropertyId, string BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool RecalculateExistingDrafts, Guid? ContractId = null,
    bool SkipUnbilledPeriods = false)
    : IRequest<Result<GenerateInvoicesResult>>;

public sealed class GenerateInvoicesCommandValidator : AbstractValidator<GenerateInvoicesCommand>
{
    public GenerateInvoicesCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.BillingMonth).BillingMonth(clock);
        RuleFor(x => x.RoomIds).Must(r => r is null || r.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
    }
}

public sealed class GenerateInvoicesHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IContractQueries contractQueries, IInvoiceRepository invoiceRepository,
    IPropertyQueries properties, IRoomQueries rooms, IRenterQueries renters, InvoiceInputLoader inputs, RoomChargeBilling roomCharges,
    BillingGapPlanner gapPlanner, IBillingGapRepository gapRepository)
    : IRequestHandler<GenerateInvoicesCommand, Result<GenerateInvoicesResult>>
{
    private const string MissingReadingCode = "MISSING_READING";

    public async ValueTask<Result<GenerateInvoicesResult>> Handle(GenerateInvoicesCommand request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var month = BillingMonths.Parse(request.BillingMonth)!.Value;
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var roomCodes = await rooms.GetCodesInPropertyAsync(request.PropertyId, request.RoomIds, request.Floor, cancellationToken);
        var roomIdList = roomCodes.Keys.ToList();

        var candidates = await contractQueries.ListBillingCandidatesAsync(roomIdList, month, monthEnd, request.ContractId, cancellationToken);

        int created = 0, recalculated = 0, withIssues = 0, gapsCreated = 0;
        var skipped = new List<SkippedContract>();
        foreach (var candidate in candidates)
        {
            var outcome = await GenerateOneAsync(
                candidate.Id, roomCodes[candidate.RoomId], month, request.RecalculateExistingDrafts, request.SkipUnbilledPeriods, cancellationToken);
            switch (outcome.Result)
            {
                case "CREATED": created++; break;
                case "RECALCULATED": recalculated++; break;
                default:
                    skipped.Add(new SkippedContract(candidate.Id, candidate.ContractNo, roomCodes[candidate.RoomId], outcome.Result, outcome.Unbilled));
                    break;
            }
            if (outcome.GapCreated)
                gapsCreated++;
            if (outcome.HasIssues)
                withIssues++;
        }
        return new GenerateInvoicesResult(created, recalculated, withIssues, skipped, gapsCreated);
    }

    private sealed record Outcome(string Result, bool HasIssues = false, bool GapCreated = false, IReadOnlyList<UnbilledPeriodDto>? Unbilled = null);

    private async Task<Outcome> GenerateOneAsync(
        Guid contractId, string roomCode, DateOnly month, bool recalculateDrafts, bool skipUnbilledPeriods, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId, ct);
        var contract = (await contracts.GetForUpdateAsync(contractId, ct))!;
        var schedule = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule;
        var period = BillingMonths.PeriodIn(contract, month, schedule);
        if (period is null)
            return new Outcome("NO_PERIOD_IN_MONTH");
        // Ngày báo trả phòng (PlannedMoveOutDate) chỉ là dấu đánh dấu: chỉ khi đã bắt đầu thanh lý (ngày trả phòng thật) kỳ đó mới thuộc hóa đơn trả phòng.
        if (contract.ActualEndDate is { } actualEnd && actualEnd <= period.End)
            return new Outcome("USE_FINAL_INVOICE");

        var invoices = await invoiceRepository.ListWithDetailsByContractAsync(contractId, ct);
        var existing = invoices.FirstOrDefault(i => i.PeriodStart == period.Start);
        if (existing is not null && (existing.Status != InvoiceStatus.Draft || !recalculateDrafts))
            return new Outcome("EXISTS");
        if (existing is null && invoices.Any(i => i.PeriodStart > period.Start))
            return new Outcome("LATER_PERIOD_BILLED");
        var plan = GapPlan.Continuous;
        if (existing is null)
        {
            plan = await gapPlanner.PlanAsync(contract, schedule, period, invoices.Select(i => (i.PeriodStart, i.PeriodEnd, i.Status)).ToList(),
                skipUnbilledPeriods, ct);
            if (plan.Error is { } gapError)
                return new Outcome(gapError.Code);
            if (plan.NeedsSkip)
                return new Outcome("PREVIOUS_PERIOD_NOT_BILLED", Unbilled: plan.UnbilledDtos);
        }
        if (existing is null && invoices.Any(i => i.Status == InvoiceStatus.Draft && i.PeriodStart < period.Start
                && i.Issues.Any(x => x.Code == MissingReadingCode)))
            return new Outcome("PREVIOUS_PERIOD_INCOMPLETE");

        var calculation = InvoiceCalculator.Calculate(
            await inputs.LoadAsync(contract, period, existing?.Id, ct, gapClosingPeriodStart: plan.ClosingPeriodStart));
        if (existing is null && calculation.Issues.Any(x => x.Code == MissingReadingCode))
            return new Outcome(MissingReadingCode);
        Invoice invoice;
        if (existing is not null)
        {
            existing.ApplyCalculation(calculation, keepManualEdits: true, period.End);
            invoice = existing;
        }
        else
        {
            var representative = (await renters.GetNamesAsync([contract.RepresentativeRenterId], ct))[contract.RepresentativeRenterId];
            invoice = Invoice.CreateDraft(contract.PropertyId, contract.RoomId, contract.Id, period,
                roomCode, contract.ContractNo, representative, calculation);
            invoiceRepository.Add(invoice);
        }
        if (plan.Skip)
            gapRepository.Add(ContractBillingGap.Create(contract.Id, plan.Unbilled, invoice.Id, null));
        await roomCharges.AttachPendingAsync(invoice, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new Outcome(existing is null ? "CREATED" : "RECALCULATED", invoice.HasBlockingIssues, plan.Skip);
    }
}

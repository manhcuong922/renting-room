using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

public sealed record CreateFinalInvoiceCommand(Guid ContractId, IReadOnlyList<MeterReadingInput>? FinalReadings, bool SkipUnbilledPeriods = false)
    : IRequest<Result<InvoiceDetailDto>>;

public sealed class CreateFinalInvoiceHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IInvoiceRepository invoiceRepository, IInvoiceQueries invoiceQueries,
    IPropertyQueries properties, IRoomQueries rooms, IRenterQueries renters, InvoiceInputLoader inputs, ContractMeterReadings meterReadings,
    RoomChargeBilling roomCharges, BillingGapPlanner gapPlanner, IBillingGapRepository gapRepository, TimeProvider clock)
    : IRequestHandler<CreateFinalInvoiceCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(CreateFinalInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(request.ContractId, cancellationToken);
        var contract = await contracts.GetForUpdateAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;
        if (contract.Status != ContractStatus.Liquidating)
            return ContractErrors.NotLiquidating;

        var invoices = await invoiceQueries.ListPeriodsAsync(contract.Id, cancellationToken);
        if (invoices.Any(i => i.Type == InvoiceType.Final))
            return BillingErrors.FinalExists;
        if (invoices.Any(i => i.Status == InvoiceStatus.Draft))
            return BillingErrors.DraftExists;

        var schedule = (await properties.GetBillingAsync(contract.PropertyId, cancellationToken)).Schedule;
        var period = contract.BillingPeriods(schedule, contract.ActualEndDate!.Value).LastOrDefault();
        if (period is null)
            return BillingErrors.FinalNotRequired;
        var plan = await gapPlanner.PlanAsync(contract, schedule, period, invoices.Select(i => (i.PeriodStart, i.PeriodEnd, i.Status)).ToList(),
            request.SkipUnbilledPeriods, cancellationToken);
        if (plan.Error is not null)
            return plan.Error;
        if (plan.NeedsSkip)
            return BillingErrors.PreviousNotBilledPeriods(plan.UnbilledDtos);

        var readings = await meterReadings.RecordAsync(
            contract, ReadingKind.Final, contract.ActualEndDate.Value, request.FinalReadings, cancellationToken);
        if (readings.IsFailure)
            return readings.Error!;
        await unitOfWork.SaveChangesAsync(cancellationToken); // chỉ số cuối phải có trong DB trước khi nạp dữ liệu tính phiếu

        var calculation = InvoiceCalculator.Calculate(
            await inputs.LoadAsync(contract, period, null, cancellationToken, InvoiceType.Final, plan.ClosingPeriodStart));
        var room = (await rooms.GetCodeAsync(contract.RoomId, cancellationToken))!;
        var representative = (await renters.GetNamesAsync([contract.RepresentativeRenterId], cancellationToken))[contract.RepresentativeRenterId];
        var invoice = Invoice.CreateDraft(contract.PropertyId, contract.RoomId, contract.Id, period, room, contract.ContractNo,
            representative, calculation, InvoiceType.Final);
        invoiceRepository.Add(invoice);
        if (plan.Skip)
            gapRepository.Add(ContractBillingGap.Create(contract.Id, plan.Unbilled, invoice.Id, null));
        await roomCharges.AttachPendingAsync(invoice, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(clock.GetUtcNow().ToBusinessDate());
    }
}

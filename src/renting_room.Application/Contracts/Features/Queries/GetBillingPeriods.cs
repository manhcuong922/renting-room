using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ContractBillingPeriodDto(DateOnly Start, DateOnly End, DateOnly Month, string BillingMonth, int Days, bool Skipped);

public sealed record GetBillingPeriodsQuery(Guid ContractId, DateOnly? Until) : IRequest<Result<IReadOnlyList<ContractBillingPeriodDto>>>;

public sealed class GetBillingPeriodsHandler(IContractQueries contracts, IPropertyQueries properties, IBillingGapRepository gaps, TimeProvider clock)
    : IRequestHandler<GetBillingPeriodsQuery, Result<IReadOnlyList<ContractBillingPeriodDto>>>
{
    private const int DefaultMonthsAhead = 12;

    public async ValueTask<Result<IReadOnlyList<ContractBillingPeriodDto>>> Handle(GetBillingPeriodsQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var until = request.Until
            ?? contract.ActualEndDate
            ?? (contract.EndDate is { } end && end >= today ? end : today.AddMonths(DefaultMonthsAhead));
        var maxUntil = contract.StartDate.AddYears(10);
        if (until > maxUntil)
            until = maxUntil;

        var schedule = (await properties.GetBillingAsync(contract.PropertyId, cancellationToken)).Schedule;
        var skipped = await gaps.ListByContractAsync(contract.Id, cancellationToken);
        IReadOnlyList<ContractBillingPeriodDto> periods = schedule.ContractPeriods(contract.BillingStartDate, contract.ActualEndDate, until)
            .Select(p => new ContractBillingPeriodDto(p.Start, p.End, p.Month, p.BillingMonth, p.Days, skipped.Any(g => g.FromDate <= p.Start && p.End <= g.ToDate)))
            .ToList();
        return Result.Success(periods);
    }
}

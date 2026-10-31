using Mediator;
using renting_room.Application.Contracts;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record BillingGapDto(Guid Id, DateOnly FromDate, DateOnly ToDate, int PeriodCount, Guid? ResumedInvoiceId, string? Note);

/// <summary>M07 BL-BR-42: các khoảng ngắt tính tiền của HĐ.</summary>
public sealed record GetBillingGapsQuery(Guid ContractId) : IRequest<Result<IReadOnlyList<BillingGapDto>>>;

public sealed class GetBillingGapsHandler(IContractQueries contracts, IBillingGapRepository gaps)
    : IRequestHandler<GetBillingGapsQuery, Result<IReadOnlyList<BillingGapDto>>>
{
    public async ValueTask<Result<IReadOnlyList<BillingGapDto>>> Handle(GetBillingGapsQuery request, CancellationToken cancellationToken)
    {
        if (await contracts.GetAsync(request.ContractId, cancellationToken) is null)
            return ContractErrors.NotFound;
        IReadOnlyList<BillingGapDto> list = (await gaps.ListByContractAsync(request.ContractId, cancellationToken))
            .Select(g => new BillingGapDto(g.Id, g.FromDate, g.ToDate, g.PeriodCount, g.ResumedInvoiceId, g.Note)).ToList();
        return Result.Success(list);
    }
}

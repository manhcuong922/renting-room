using FluentValidation;
using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ListContractsQuery(
    Guid? PropertyId,
    Guid? RoomId,
    Guid? RenterId,
    ContractStatus? Status,
    int? ExpiringWithinDays,
    bool? Overdue,
    string? Search,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    bool? HasDeposit = null,
    bool? MissingSignedDocument = null,
    bool? DepositNotRefunded = null) : IRequest<PagedResult<ContractSummaryDto>>;

public sealed class ListContractsQueryValidator : AbstractValidator<ListContractsQuery>
{
    public ListContractsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.ExpiringWithinDays).InclusiveBetween(0, 365).When(x => x.ExpiringWithinDays is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.Search).OptionalText(30);
    }
}

public sealed class ListContractsHandler(IContractQueries contractQueries, TimeProvider clock)
    : IRequestHandler<ListContractsQuery, PagedResult<ContractSummaryDto>>
{
    public async ValueTask<PagedResult<ContractSummaryDto>> Handle(ListContractsQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var page = await contractQueries.ListAsync(request, today, cancellationToken);
        var items = page.Items;

        var flags = await contractQueries.GetFlagsAsync(items.Select(i => i.Id), today, cancellationToken);
        return page with { Items = items.Select(i => i with { Flags = flags.GetValueOrDefault(i.Id, []) }).ToList() };
    }
}

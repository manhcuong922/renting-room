using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;

namespace renting_room.Application.Properties;

public sealed record ListPropertiesQuery(string? Search, bool IncludeArchived, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<PropertySummaryDto>>;

public sealed class ListPropertiesQueryValidator : AbstractValidator<ListPropertiesQuery>
{
    public ListPropertiesQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Search).OptionalText(100);
    }
}

public sealed class ListPropertiesHandler(IPropertyQueries properties, TimeProvider clock)
    : IRequestHandler<ListPropertiesQuery, PagedResult<PropertySummaryDto>>
{
    public async ValueTask<PagedResult<PropertySummaryDto>> Handle(ListPropertiesQuery request, CancellationToken cancellationToken) =>
        await properties.ListAsync(request, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
}

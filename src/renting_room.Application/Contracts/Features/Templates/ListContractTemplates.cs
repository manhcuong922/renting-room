using Mediator;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ListContractTemplatesQuery(ContractType? Type, bool IncludeArchived) : IRequest<IReadOnlyList<ContractTemplateDto>>;

public sealed class ListContractTemplatesHandler(IContractTemplateQueries templateQueries)
    : IRequestHandler<ListContractTemplatesQuery, IReadOnlyList<ContractTemplateDto>>
{
    public async ValueTask<IReadOnlyList<ContractTemplateDto>> Handle(ListContractTemplatesQuery request, CancellationToken cancellationToken)
    {
        var templates = await templateQueries.ListAsync(request.Type, request.IncludeArchived, cancellationToken);
        return templates.Select(ContractTemplateDto.From).ToList();
    }
}

using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record GetContractTemplateQuery(Guid Id) : IRequest<Result<ContractTemplateDto>>;

public sealed class GetContractTemplateHandler(IContractTemplateQueries templates) : IRequestHandler<GetContractTemplateQuery, Result<ContractTemplateDto>>
{
    public async ValueTask<Result<ContractTemplateDto>> Handle(GetContractTemplateQuery request, CancellationToken cancellationToken)
    {
        var template = await templates.GetAsync(request.Id, cancellationToken);
        return template is null ? ContractTemplateErrors.NotFound : ContractTemplateDto.From(template);
    }
}

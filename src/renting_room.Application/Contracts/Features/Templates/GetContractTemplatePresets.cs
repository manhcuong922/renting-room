using Mediator;

namespace renting_room.Application.Contracts;

public sealed record GetContractTemplatePresetsQuery : IRequest<IReadOnlyList<ContractTemplateInput>>;

public sealed class GetContractTemplatePresetsHandler : IRequestHandler<GetContractTemplatePresetsQuery, IReadOnlyList<ContractTemplateInput>>
{
    public ValueTask<IReadOnlyList<ContractTemplateInput>> Handle(GetContractTemplatePresetsQuery request, CancellationToken cancellationToken) =>
        new(ContractTemplatePresets.All);
}

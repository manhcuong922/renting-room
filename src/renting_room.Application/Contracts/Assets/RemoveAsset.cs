using Mediator;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record RemoveAssetCommand(Guid ContractId, Guid AssetId) : IRequest<Result>;

public sealed class RemoveAssetHandler(ContractMutation mutation) : IRequestHandler<RemoveAssetCommand, Result>
{
    public ValueTask<Result> Handle(RemoveAssetCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId,
            contract => Task.FromResult(contract.RemoveAsset(request.AssetId)), cancellationToken));
}

using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record RemoveAssetCommand(Guid ContractId, Guid AssetId) : IRequest<Result>;

public sealed class RemoveAssetHandler(IAppDbContext db) : IRequestHandler<RemoveAssetCommand, Result>
{
    public ValueTask<Result> Handle(RemoveAssetCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.RemoveAsset(request.AssetId)), cancellationToken));
}

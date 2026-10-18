using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record UpdateAssetCommand(Guid ContractId, Guid AssetId, AssetRequest Asset) : IRequest<Result>;

public sealed class UpdateAssetCommandValidator : AbstractValidator<UpdateAssetCommand>
{
    public UpdateAssetCommandValidator()
    {
        RuleFor(x => x.Asset).NotNull();
        RuleFor(x => x.Asset).FlattenedValidator(new AssetRequestValidator());
    }
}

public sealed class UpdateAssetHandler(IAppDbContext db) : IRequestHandler<UpdateAssetCommand, Result>
{
    public ValueTask<Result> Handle(UpdateAssetCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.UpdateAsset(request.AssetId, request.Asset.ToDomain())), cancellationToken));
}

using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record AddAssetCommand(Guid ContractId, AssetRequest Asset) : IRequest<Result<Guid>>;

public sealed class AddAssetCommandValidator : AbstractValidator<AddAssetCommand>
{
    public AddAssetCommandValidator()
    {
        RuleFor(x => x.Asset).NotNull();
        RuleFor(x => x.Asset).FlattenedValidator(new AssetRequestValidator());
    }
}

public sealed class AddAssetHandler(ContractMutation mutation) : IRequestHandler<AddAssetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(AddAssetCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, contract =>
        {
            var added = contract.AddAsset(request.Asset.ToDomain());
            return Task.FromResult(added.IsSuccess ? Result.Success(added.Value!.Id) : Result.Failure<Guid>(added.Error!));
        }, cancellationToken));
}

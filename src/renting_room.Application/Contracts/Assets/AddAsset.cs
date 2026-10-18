using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

// Tài sản bàn giao
public sealed record AddAssetCommand(Guid ContractId, AssetRequest Asset) : IRequest<Result<Guid>>;

public sealed class AddAssetCommandValidator : AbstractValidator<AddAssetCommand>
{
    public AddAssetCommandValidator()
    {
        RuleFor(x => x.Asset).NotNull();
        RuleFor(x => x.Asset).FlattenedValidator(new AssetRequestValidator());
    }
}

public sealed class AddAssetHandler(IAppDbContext db) : IRequestHandler<AddAssetCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(AddAssetCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, contract =>
        {
            var added = contract.AddAsset(request.Asset.ToDomain());
            return Task.FromResult(added.IsSuccess ? Result.Success(added.Value!.Id) : Result.Failure<Guid>(added.Error!));
        }, cancellationToken));
}

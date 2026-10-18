using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record ExtendContractCommand(Guid ContractId, DateOnly NewEndDate) : IRequest<Result>;

public sealed class ExtendContractCommandValidator : AbstractValidator<ExtendContractCommand>
{
    public ExtendContractCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.NewEndDate)
            .Must(d => d <= clock.GetUtcNow().ToBusinessDate().AddYears(10))
            .WithErrorCode("INVALID_END_DATE").WithMessage("Gia hạn tối đa 10 năm kể từ hôm nay.");
}

public sealed class ExtendContractHandler(IAppDbContext db) : IRequestHandler<ExtendContractCommand, Result>
{
    public ValueTask<Result> Handle(ExtendContractCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.Extend(request.NewEndDate)), cancellationToken));
}

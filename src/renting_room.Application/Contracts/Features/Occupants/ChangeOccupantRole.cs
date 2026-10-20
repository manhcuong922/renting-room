using FluentValidation;
using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record ChangeOccupantRoleCommand(Guid ContractId, Guid OccupantId, OccupantRole Role) : IRequest<Result>;

public sealed class ChangeOccupantRoleCommandValidator : AbstractValidator<ChangeOccupantRoleCommand>
{
    public ChangeOccupantRoleCommandValidator() => RuleFor(x => x.Role).IsInEnum();
}

public sealed class ChangeOccupantRoleHandler(ContractMutation mutation, OccupantChecks occupantChecks) : IRequestHandler<ChangeOccupantRoleCommand, Result>
{
    public ValueTask<Result> Handle(ChangeOccupantRoleCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            var occupant = contract.Occupants.FirstOrDefault(o => o.Id == request.OccupantId);
            if (occupant is not null && request.Role == OccupantRole.CoTenant && occupant.RenterId != contract.RepresentativeRenterId
                && !await occupantChecks.HasIdNumberAsync(occupant.RenterId, cancellationToken))
                return Result.Failure(ContractErrors.CoTenantIdRequired);

            var changed = contract.ChangeOccupantRole(request.OccupantId, request.Role);
            if (changed.IsFailure || contract.Status != ContractStatus.Active)
                return changed;
            return await occupantChecks.FindRoleConflictAsync(contract, cancellationToken) is { } conflict ? Result.Failure(conflict) : changed;
        }, cancellationToken));
}

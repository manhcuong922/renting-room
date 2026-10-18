using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

// Đồng thuê (CT-BR-49)
public sealed record ChangeOccupantRoleCommand(Guid ContractId, Guid OccupantId, OccupantRole Role) : IRequest<Result>;

public sealed class ChangeOccupantRoleCommandValidator : AbstractValidator<ChangeOccupantRoleCommand>
{
    public ChangeOccupantRoleCommandValidator() => RuleFor(x => x.Role).IsInEnum();
}

/// <summary>CT-BR-49: đổi người ở ↔ đồng thuê. Đồng thuê phải có giấy tờ; về người ở thường thì kiểm CT-BR-50.</summary>
public sealed class ChangeOccupantRoleHandler(IAppDbContext db) : IRequestHandler<ChangeOccupantRoleCommand, Result>
{
    public ValueTask<Result> Handle(ChangeOccupantRoleCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var occupant = contract.Occupants.FirstOrDefault(o => o.Id == request.OccupantId);
            if (occupant is not null && request.Role == OccupantRole.CoTenant && occupant.RenterId != contract.RepresentativeRenterId
                && !await OccupantChecks.HasIdNumberAsync(db, occupant.RenterId, cancellationToken))
                return Result.Failure(ContractErrors.CoTenantIdRequired);

            var changed = contract.ChangeOccupantRole(request.OccupantId, request.Role);
            if (changed.IsFailure || contract.Status != ContractStatus.Active)
                return changed;
            return await OccupantChecks.FindRoleConflictAsync(db, contract, cancellationToken) is { } conflict ? Result.Failure(conflict) : changed;
        }, cancellationToken));
}

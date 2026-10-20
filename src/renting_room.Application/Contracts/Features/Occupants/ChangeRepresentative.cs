using FluentValidation;
using Mediator;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

/// <param name="HouseholdHeadRenterId">Chủ hộ mới (phải đang ở); null = giữ chủ hộ hiện tại, không có thì người đứng tên mới.</param>
/// <param name="OldRepresentativeMovesOut">Người đứng tên cũ rời đi cùng ngày ⇒ ghi chuyển đi + kết thúc xe của họ.</param>
public sealed record ChangeRepresentativeCommand(
    Guid ContractId, Guid RenterId, DateOnly Date, Guid? HouseholdHeadRenterId, bool OldRepresentativeMovesOut) : IRequest<Result>;

public sealed class ChangeRepresentativeCommandValidator : AbstractValidator<ChangeRepresentativeCommand>
{
    public ChangeRepresentativeCommandValidator() => RuleFor(x => x.RenterId).NotEmpty().WithErrorCode("REQUIRED");
}

public sealed class ChangeRepresentativeHandler(ContractMutation mutation, IRenterQueries renters, OccupantChecks occupantChecks, TimeProvider clock) : IRequestHandler<ChangeRepresentativeCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeRepresentativeCommand request, CancellationToken cancellationToken)
    {
        var renter = await renters.FindActiveAsync(request.RenterId, cancellationToken);
        if (renter is null)
            return Result.Failure(RenterErrors.RenterNotFound);

        return await mutation.RunAsync(request.ContractId, async contract =>
        {
            if (renter.Id != contract.RepresentativeRenterId && renter.IdNumberHash is null)
                return Result.Failure(ContractErrors.RepresentativeIdRequired);

            var snapshot = SigningSnapshot.FromJson(contract.SigningSnapshot)?.WithRepresentative(renter).ToJson();
            var changed = contract.ChangeRepresentative(renter.Id, request.Date, clock.GetUtcNow().ToBusinessDate(), request.HouseholdHeadRenterId,
                request.OldRepresentativeMovesOut, snapshot);
            if (changed.IsFailure)
                return changed;
            return await occupantChecks.FindRoleConflictAsync(contract, cancellationToken) is { } conflict ? Result.Failure(conflict) : changed;
        }, cancellationToken);
    }
}

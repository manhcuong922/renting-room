using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

// Người ở
public sealed record AddOccupantCommand(
    Guid ContractId, Guid RenterId, DateOnly MoveInDate, DateOnly? ExpectedEndDate, string? Relationship, string? Note,
    OccupantRelationship? RelationshipType = null, bool GuardianConsent = false, OccupantRole Role = OccupantRole.Resident) : IRequest<Result>;

public sealed class AddOccupantCommandValidator : AbstractValidator<AddOccupantCommand>
{
    public AddOccupantCommandValidator()
    {
        RuleFor(x => x.RenterId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.ExpectedEndDate).Must((x, d) => d is null || d >= x.MoveInDate).WithErrorCode("INVALID_DATE");
        RuleFor(x => x.Relationship).OptionalText(50);
        RuleFor(x => x.RelationshipType).IsInEnum().When(x => x.RelationshipType is not null);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-BR-28..30: quan hệ với người đứng tên hợp lệ; CT-BR-31: HĐ đang hiệu lực ⇒ người mới không đang ở phòng khác;
/// CT-BR-49: đồng thuê phải có giấy tờ; CT-BR-50: không vừa là người ở thường ở đây vừa đứng tên HĐ khác.
/// </summary>
public sealed class AddOccupantHandler(IAppDbContext db)
    : IRequestHandler<AddOccupantCommand, Result>
{
    public ValueTask<Result> Handle(AddOccupantCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var people = await OccupantChecks.LoadPeopleAsync(db, [request.RenterId, contract.ReferenceRenterId], cancellationToken);
            if (!people.ContainsKey(request.RenterId))
                return Result.Failure(RenterErrors.RenterNotFound);

            var isReference = request.RenterId == contract.ReferenceRenterId;
            var input = new OccupantInput(request.RenterId, request.MoveInDate, request.ExpectedEndDate, request.Relationship, request.Note,
                isReference ? null : request.RelationshipType, request.GuardianConsent, request.Role);
            if (request.Role == OccupantRole.CoTenant && request.RenterId != contract.RepresentativeRenterId
                && !await OccupantChecks.HasIdNumberAsync(db, request.RenterId, cancellationToken))
                return Result.Failure(ContractErrors.CoTenantIdRequired);

            if (contract.Status == ContractStatus.Active)
            {
                var elsewhere = await OccupantChecks.FindLivingElsewhereAsync(db, contract,
                    [(request.RenterId, request.MoveInDate, null)], people.ToDictionary(p => p.Key, p => p.Value.Name), cancellationToken);
                if (elsewhere is not null)
                    return Result.Failure(elsewhere);
            }

            var added = contract.AddOccupant(input);
            if (added.IsFailure || contract.Status != ContractStatus.Active)
                return added;
            return await OccupantChecks.FindRoleConflictAsync(db, contract, cancellationToken) is { } conflict ? Result.Failure(conflict) : added;
        }, cancellationToken));
}

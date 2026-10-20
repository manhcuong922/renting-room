using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record MoveOccupantCommand(
    Guid ContractId, Guid OccupantId, Guid ToContractId, DateOnly Date, OccupantRole Role, OccupantRelationship? RelationshipType,
    string? Relationship, bool GuardianConsent, string? Note) : IRequest<Result>;

public sealed class MoveOccupantCommandValidator : AbstractValidator<MoveOccupantCommand>
{
    public MoveOccupantCommandValidator()
    {
        RuleFor(x => x.ToContractId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.RelationshipType).IsInEnum().When(x => x.RelationshipType is not null);
        RuleFor(x => x.Relationship).OptionalText(50);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

/// <summary>
/// CT-UC-26 / CT-BR-52: người đi, HĐ ở lại — 1 transaction, khóa 2 HĐ theo id (C-07): ghi chuyển đi ở HĐ nguồn, thêm vào HĐ đích đang hiệu lực
/// cùng ngày, xe của người đó chuyển theo. Khoản thu chi không đi theo người.
/// </summary>
public sealed class MoveOccupantHandler(IContractQueries contracts, IUnitOfWork unitOfWork, ISender sender) : IRequestHandler<MoveOccupantCommand, Result>
{
    public async ValueTask<Result> Handle(MoveOccupantCommand request, CancellationToken cancellationToken)
    {
        if (request.ToContractId == request.ContractId)
            return Result.Failure(ContractErrors.MoveSameContract);
        var source = await contracts.FindOccupantAsync(request.ContractId, request.OccupantId, cancellationToken);
        var targetStatus = await contracts.GetStatusAsync(request.ToContractId, cancellationToken);
        if (source is null || targetStatus is null)
            return Result.Failure(ContractErrors.NotFound);
        if (source.RenterId is not { } renterId)
            return Result.Failure(ContractErrors.OccupantNotFound);
        if (targetStatus != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { request.ContractId, request.ToContractId }.Order())
            await unitOfWork.LockForUpdateAsync<Contract>(id, cancellationToken);

        var left = await sender.Send(new EndOccupancyCommand(request.ContractId, request.OccupantId, request.Date), cancellationToken);
        if (left.IsFailure)
            return left;
        var arrived = await sender.Send(new AddOccupantCommand(request.ToContractId, renterId, request.Date, null, request.Relationship, request.Note,
            request.RelationshipType, request.GuardianConsent, request.Role), cancellationToken);
        if (arrived.IsFailure)
            return arrived;

        var vehicles = await contracts.ListOpenVehiclesAsync(request.ContractId, renterId, cancellationToken);
        foreach (var vehicle in vehicles)
        {
            var ended = await sender.Send(new EndVehicleCommand(request.ContractId, vehicle.Id, request.Date), cancellationToken);
            if (ended.IsFailure)
                return ended;
            var registered = await sender.Send(new RegisterVehicleCommand(request.ToContractId, renterId, vehicle.VehicleType, vehicle.PlateNumber,
                vehicle.BrandColor, request.Date, vehicle.Note), cancellationToken);
            if (registered.IsFailure)
                return Result.Failure(registered.Error!);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

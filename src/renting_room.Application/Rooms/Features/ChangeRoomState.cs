using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Properties;

namespace renting_room.Application.Rooms;

public sealed record ChangeRoomStateCommand(Guid Id, RoomAction Action, string? Note = null) : IRequest<Result>;

public sealed class ChangeRoomStateCommandValidator : AbstractValidator<ChangeRoomStateCommand>
{
    public ChangeRoomStateCommandValidator() => RuleFor(x => x.Note).OptionalText(500);
}

/// <summary>PR-BR-03/05: khóa hàng phòng ⇒ không chạy song song với kích hoạt hợp đồng trên cùng phòng.</summary>
public sealed class ChangeRoomStateHandler(
    IRoomRepository rooms, IRoomQueries queries, IPropertyQueries properties, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ChangeRoomStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeRoomStateCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var room = await rooms.GetForUpdateAsync(request.Id, cancellationToken);
        if (room is null)
            return Result.Failure(PropertyErrors.RoomNotFound);

        Result result;
        switch (request.Action)
        {
            case RoomAction.StartMaintenance:
                if (await queries.HasOpenContractAsync(room.Id, includeDraft: false, clock.GetUtcNow().ToBusinessDate(), cancellationToken))
                    return Result.Failure(PropertyErrors.RoomOccupied);
                result = room.StartMaintenance(request.Note);
                break;
            case RoomAction.EndMaintenance:
                result = room.EndMaintenance();
                break;
            case RoomAction.Archive:
                if (await queries.HasOpenContractAsync(room.Id, includeDraft: true, clock.GetUtcNow().ToBusinessDate(), cancellationToken))
                    return Result.Failure(PropertyErrors.RoomHasContracts);
                result = room.Archive(clock.GetUtcNow());
                break;
            default:
                var propertyArchived = (await properties.GetStatusAsync(room.PropertyId, cancellationToken))?.IsArchived == true;
                result = propertyArchived ? Result.Failure(PropertyErrors.PropertyArchived) : room.Restore();
                break;
        }

        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

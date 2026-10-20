using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record SetRoomGroupMembersCommand(Guid Id, IReadOnlyList<Guid> RoomIds) : IRequest<Result<RoomGroupDto>>;

public sealed class SetRoomGroupMembersCommandValidator : AbstractValidator<SetRoomGroupMembersCommand>
{
    public SetRoomGroupMembersCommandValidator() =>
        RuleFor(x => x.RoomIds).NotNull().Must(ids => ids.Count <= 500).WithErrorCode("OUT_OF_RANGE");
}

public sealed class SetRoomGroupMembersHandler(IRoomGroupRepository groups, IRoomQueries rooms, IUnitOfWork unitOfWork)
    : IRequestHandler<SetRoomGroupMembersCommand, Result<RoomGroupDto>>
{
    public async ValueTask<Result<RoomGroupDto>> Handle(SetRoomGroupMembersCommand request, CancellationToken cancellationToken)
    {
        var group = await groups.GetAsync(request.Id, cancellationToken);
        if (group is null)
            return PropertyErrors.RoomGroupNotFound;

        var roomIds = request.RoomIds.Distinct().ToList();
        var validCount = await rooms.CountInPropertyAsync(roomIds, group.PropertyId, cancellationToken);
        if (validCount != roomIds.Count)
            return PropertyErrors.RoomNotInProperty;

        group.SetMembers(roomIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RoomGroupDto(group.Id, group.PropertyId, group.Name, group.Description, group.Members.Select(m => m.RoomId).ToList());
    }
}

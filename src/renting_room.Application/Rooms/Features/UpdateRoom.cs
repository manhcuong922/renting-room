using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record UpdateRoomCommand(Guid Id, string Code, RoomSpecInput Spec, uint Version) : IRequest<Result<RoomDto>>;

public sealed class UpdateRoomCommandValidator : AbstractValidator<UpdateRoomCommand>
{
    public UpdateRoomCommandValidator()
    {
        RuleFor(x => x.Code).Code(20, "Mã phòng");
        RuleFor(x => x.Spec).NotNull().WithErrorCode("REQUIRED");
        When(x => x.Spec is not null, () => this.SpecRules(x => x.Spec, prefix: "spec."));
    }
}

public sealed class UpdateRoomHandler(IRoomRepository rooms, IRoomQueries queries, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<UpdateRoomCommand, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(UpdateRoomCommand request, CancellationToken cancellationToken)
    {
        var room = await rooms.GetAsync(request.Id, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;

        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await rooms.IsCodeTakenAsync(room.PropertyId, code, excludeId: room.Id, cancellationToken))
            return PropertyErrors.RoomCodeTaken;

        unitOfWork.SetExpectedVersion(room, request.Version);
        room.Update(code, request.Spec.ToDomain());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await queries.GetAsync(room.Id, clock.GetUtcNow().ToBusinessDate(), cancellationToken))!;
    }
}

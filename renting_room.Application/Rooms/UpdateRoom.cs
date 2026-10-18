using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
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

public sealed class UpdateRoomHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateRoomCommand, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(UpdateRoomCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;

        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await db.Rooms.AnyAsync(r => r.PropertyId == room.PropertyId && r.Code == code && r.Id != room.Id, cancellationToken))
            return PropertyErrors.RoomCodeTaken;

        db.SetExpectedVersion(room, request.Version);
        room.Update(code, request.Spec.ToDomain());
        await db.SaveChangesAsync(cancellationToken);

        var today = clock.GetUtcNow().ToBusinessDate();
        var row = await db.Rooms.AsNoTracking().Where(r => r.Id == room.Id).ToRows(db, today).FirstAsync(cancellationToken);
        return (await new[] { row }.ToDtosAsync(db, today, cancellationToken))[0];
    }
}

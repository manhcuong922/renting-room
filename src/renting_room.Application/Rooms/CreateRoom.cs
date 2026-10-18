using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

// Tạo phòng
public sealed record CreateRoomCommand(Guid PropertyId, string Code, RoomSpecInput Spec) : IRequest<Result<Guid>>;

public sealed class CreateRoomCommandValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomCommandValidator()
    {
        RuleFor(x => x.Code).Code(20, "Mã phòng");
        RuleFor(x => x.Spec).NotNull().WithErrorCode("REQUIRED");
        When(x => x.Spec is not null, () => this.SpecRules(x => x.Spec, prefix: "spec."));
    }
}

public sealed class CreateRoomHandler(IAppDbContext db) : IRequestHandler<CreateRoomCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        if (property.IsArchived)
            return PropertyErrors.PropertyArchived;

        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await db.Rooms.AnyAsync(r => r.PropertyId == property.Id && r.Code == code, cancellationToken))
            return PropertyErrors.RoomCodeTaken;

        var room = Room.Create(property.Id, code, request.Spec.ToDomain());
        db.Rooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);
        return room.Id;
    }
}

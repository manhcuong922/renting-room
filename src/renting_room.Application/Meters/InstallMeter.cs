using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record InstallMeterCommand(
    Guid RoomId, Guid FeeTypeId, string? SerialNo, DateOnly InstalledDate, decimal InitialValue, string? Note) : IRequest<Result<Guid>>;

public sealed class InstallMeterCommandValidator : AbstractValidator<InstallMeterCommand>
{
    public InstallMeterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.FeeTypeId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.SerialNo).OptionalText(50);
        RuleFor(x => x.InstalledDate).ReadingDate(clock);
        RuleFor(x => x.InitialValue).ReadingValue();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>MT-UC-01: lắp công tơ cho phòng — khoản thu phải là điện nước theo công tơ, cùng khu, chưa ngừng dùng (MT-BR-01/02).</summary>
public sealed class InstallMeterHandler(IAppDbContext db) : IRequestHandler<InstallMeterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(InstallMeterCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;
        if (room.IsArchived)
            return PropertyErrors.RoomArchived;

        var fee = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != room.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;
        if (fee.IsArchived)
            return FeeErrors.Archived;
        if (await db.Meters.AnyAsync(m => m.RoomId == room.Id && m.FeeTypeId == fee.Id && m.RemovedDate == null, cancellationToken))
            return MeterErrors.AlreadyActive;

        var meter = Meter.Install(room.PropertyId, room.Id, fee.Id, request.SerialNo, request.InstalledDate, request.InitialValue, request.Note);
        db.Meters.Add(meter);
        await db.SaveChangesAsync(cancellationToken); // unique "1 công tơ hoạt động" là chốt chặn khi 2 request song song
        return meter.Id;
    }
}

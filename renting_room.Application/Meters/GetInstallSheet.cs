using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record InstallSheetRowDto(Guid RoomId, string RoomCode, string? Floor);

public sealed record GetInstallSheetQuery(Guid PropertyId, Guid FeeTypeId) : IRequest<Result<IReadOnlyList<InstallSheetRowDto>>>;

/// <summary>Các phòng đang dùng của khu chưa có công tơ đang hoạt động của khoản thu — đầu vào cho lệnh lắp hàng loạt.</summary>
public sealed class GetInstallSheetHandler(IAppDbContext db) : IRequestHandler<GetInstallSheetQuery, Result<IReadOnlyList<InstallSheetRowDto>>>
{
    public async ValueTask<Result<IReadOnlyList<InstallSheetRowDto>>> Handle(GetInstallSheetQuery request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != request.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;

        var metered = (await db.Meters.Where(m => m.PropertyId == request.PropertyId && m.FeeTypeId == fee.Id && m.RemovedDate == null)
            .Select(m => m.RoomId).ToListAsync(cancellationToken)).ToHashSet();
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.PropertyId == request.PropertyId).ToListAsync(cancellationToken);
        return rooms.Where(r => !r.IsArchived && !metered.Contains(r.Id))
            .OrderBy(r => r.Floor).ThenBy(r => r.Code)
            .Select(r => new InstallSheetRowDto(r.Id, r.Code, r.Floor)).ToList();
    }
}

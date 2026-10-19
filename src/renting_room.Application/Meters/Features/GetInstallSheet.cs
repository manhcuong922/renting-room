using FluentValidation;
using Mediator;
using renting_room.Application.Fees;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

// MT-UC-01: lưới lắp hàng loạt — phòng chưa có công tơ của khoản thu
public sealed record InstallSheetRowDto(Guid RoomId, string RoomCode, string? Floor);

public sealed record GetInstallSheetQuery(Guid PropertyId, Guid FeeTypeId) : IRequest<Result<IReadOnlyList<InstallSheetRowDto>>>;

/// <summary>Các phòng đang dùng của khu chưa có công tơ đang hoạt động của khoản thu — đầu vào cho lệnh lắp hàng loạt.</summary>
public sealed class GetInstallSheetHandler(IFeeQueries fees, IMeterQueries meters, IRoomQueries roomQueries)
    : IRequestHandler<GetInstallSheetQuery, Result<IReadOnlyList<InstallSheetRowDto>>>
{
    public async ValueTask<Result<IReadOnlyList<InstallSheetRowDto>>> Handle(GetInstallSheetQuery request, CancellationToken cancellationToken)
    {
        var fee = await fees.FindTypeAsync(request.FeeTypeId, cancellationToken);
        if (fee is null || fee.PropertyId != request.PropertyId)
            return FeeErrors.NotInProperty;
        if (fee.Group != FeeGroup.Metered)
            return MeterErrors.FeeNotMetered;

        var metered = await meters.GetMeteredRoomIdsInPropertyAsync(request.PropertyId, fee.Id, cancellationToken);
        var rooms = await roomQueries.ListByPropertyAsync(request.PropertyId, null, cancellationToken);
        return rooms.Where(r => !r.IsArchived && !metered.Contains(r.Id))
            .OrderBy(r => r.Floor).ThenBy(r => r.Code)
            .Select(r => new InstallSheetRowDto(r.Id, r.Code, r.Floor)).ToList();
    }
}

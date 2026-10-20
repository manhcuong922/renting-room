using Mediator;
using renting_room.Application.Fees;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record VacantUsageRow(
    Guid RoomId, string RoomCode, Guid MeterId, string? SerialNo, Guid FeeTypeId, string FeeTypeName, string Unit, DateOnly FromDate,
    decimal FromValue, DateOnly ToDate, decimal ToValue, decimal Quantity);

public sealed record VacantUsageTotal(Guid FeeTypeId, string FeeTypeName, string Unit, decimal Quantity);

public sealed record VacantUsageDto(DateOnly From, DateOnly To, IReadOnlyList<VacantUsageRow> Rows, IReadOnlyList<VacantUsageTotal> Totals);

public sealed record GetVacantUsageQuery(Guid PropertyId, DateOnly? From, DateOnly? To) : IRequest<Result<VacantUsageDto>>;

public sealed class GetVacantUsageHandler(
    IPropertyQueries properties, IMeterQueries meterQueries, IRoomQueries roomQueries, IFeeQueries feeQueries, TimeProvider clock)
    : IRequestHandler<GetVacantUsageQuery, Result<VacantUsageDto>>
{
    public async ValueTask<Result<VacantUsageDto>> Handle(GetVacantUsageQuery request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var to = request.To ?? clock.GetUtcNow().ToBusinessDate();
        var from = request.From ?? to.AddYears(-1);
        var meters = await meterQueries.ListByPropertyAsync(request.PropertyId, cancellationToken);
        var roomIds = meters.Select(m => m.RoomId).Distinct().ToList();
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var rooms = await roomQueries.GetCodesAsync(roomIds, cancellationToken);
        var fees = await feeQueries.GetTypesAsync(feeIds, cancellationToken);

        var rows = new List<VacantUsageRow>();
        foreach (var meter in meters)
        {
            var ordered = meter.Ordered.ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var (previous, handover) = (ordered[i - 1], ordered[i]);
                if (handover.Kind != ReadingKind.Handover || handover.Value <= previous.Value || handover.ReadingDate < from || handover.ReadingDate > to)
                    continue;
                var fee = fees[meter.FeeTypeId];
                rows.Add(new VacantUsageRow(meter.RoomId, rooms.GetValueOrDefault(meter.RoomId, string.Empty), meter.Id, meter.SerialNo, fee.Id, fee.Name,
                    fee.Unit, previous.ReadingDate, previous.Value, handover.ReadingDate, handover.Value, handover.Value - previous.Value));
            }
        }
        var totals = rows.GroupBy(r => r.FeeTypeId)
            .Select(g => new VacantUsageTotal(g.Key, g.First().FeeTypeName, g.First().Unit, g.Sum(r => r.Quantity))).OrderBy(t => t.FeeTypeName).ToList();
        return new VacantUsageDto(from, to, rows.OrderBy(r => r.RoomCode).ThenBy(r => r.ToDate).ToList(), totals);
    }
}

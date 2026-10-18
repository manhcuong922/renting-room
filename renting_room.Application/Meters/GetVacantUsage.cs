using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

/// <summary>Một khoảng công tơ chạy khi phòng không có người thuê: từ chỉ số liền trước tới chỉ số nhận phòng của HĐ kế tiếp.</summary>
public sealed record VacantUsageRow(
    Guid RoomId, string RoomCode, Guid MeterId, string? SerialNo, Guid FeeTypeId, string FeeTypeName, string Unit, DateOnly FromDate,
    decimal FromValue, DateOnly ToDate, decimal ToValue, decimal Quantity);

public sealed record VacantUsageTotal(Guid FeeTypeId, string FeeTypeName, string Unit, decimal Quantity);

public sealed record VacantUsageDto(DateOnly From, DateOnly To, IReadOnlyList<VacantUsageRow> Rows, IReadOnlyList<VacantUsageTotal> Totals);

/// <param name="From">Mặc định 12 tháng trước hôm nay.</param>
public sealed record GetVacantUsageQuery(Guid PropertyId, DateOnly? From, DateOnly? To) : IRequest<Result<VacantUsageDto>>;

/// <summary>
/// MT-BR-14: mỗi chỉ số nhận phòng cao hơn chỉ số liền trước trên cùng công tơ là phần điện nước dùng lúc phòng trống (sửa chữa, thử phòng) —
/// không nằm trên phiếu của ai. Tổng hợp theo ngày nhận phòng để chủ trọ đối chiếu hóa đơn điện nước của khu.
/// </summary>
public sealed class GetVacantUsageHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetVacantUsageQuery, Result<VacantUsageDto>>
{
    public async ValueTask<Result<VacantUsageDto>> Handle(GetVacantUsageQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var to = request.To ?? clock.GetUtcNow().ToBusinessDate();
        var from = request.From ?? to.AddYears(-1);
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => m.PropertyId == request.PropertyId).ToListAsync(cancellationToken);
        var roomIds = meters.Select(m => m.RoomId).Distinct().ToList();
        var feeIds = meters.Select(m => m.FeeTypeId).Distinct().ToList();
        var rooms = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
        var fees = await db.FeeTypes.AsNoTracking().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

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

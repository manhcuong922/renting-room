using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

public sealed record DataReviewIssue(
    Guid ContractId, string ContractNo, string PropertyCode, string RoomCode, string Code, string Message, Guid? RenterId, string? RenterName);

/// <summary>
/// CT-UC-20 (Q10): rà HĐ đang hiệu lực / đang thanh lý tìm dữ liệu cần xem lại — thường do sửa hồ sơ sau khi ký hoặc dữ liệu cũ.
/// Chỉ đọc, không sửa gì. Gồm: quan hệ người ở không còn hợp lý (CT-BR-28..30), người đứng tên &lt; 18 tuổi tại ngày ký,
/// một người ở 2 phòng (CT-BR-31), khoản thu chưa có giá (dịch vụ của HĐ + điện nước theo công tơ của phòng — sẽ không lập được phiếu, M07).
/// </summary>
public sealed record GetContractDataReviewQuery(Guid? PropertyId) : IRequest<IReadOnlyList<DataReviewIssue>>;

public sealed class GetContractDataReviewHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<GetContractDataReviewQuery, IReadOnlyList<DataReviewIssue>>
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Active, ContractStatus.Liquidating];

    public async ValueTask<IReadOnlyList<DataReviewIssue>> Handle(GetContractDataReviewQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var query = db.Contracts.AsNoTracking().Include(c => c.Occupants).Include(c => c.Fees).AsSplitQuery()
            .Where(c => OpenStatuses.Contains(c.Status));
        if (request.PropertyId is { } propertyId)
            query = query.Where(c => c.PropertyId == propertyId);
        var contracts = await query.ToListAsync(cancellationToken);
        if (contracts.Count == 0)
            return [];

        var codes = await LoadCodesAsync(contracts, cancellationToken);
        var renterIds = contracts.SelectMany(c => c.Occupants.Select(o => o.RenterId).Append(c.RepresentativeRenterId).Append(c.ReferenceRenterId)).Distinct().ToList();
        var people = await OccupantChecks.LoadPeopleAsync(db, renterIds, cancellationToken);
        var feeIds = contracts.SelectMany(c => c.Fees.Select(f => f.FeeTypeId)).Distinct().ToList();
        var roomIds = contracts.Select(c => c.RoomId).Distinct().ToList();
        var roomMeters = (await db.Meters.AsNoTracking().Where(m => roomIds.Contains(m.RoomId) && m.RemovedDate == null)
                .Select(m => new { m.RoomId, m.FeeTypeId }).ToListAsync(cancellationToken))
            .ToLookup(m => m.RoomId, m => m.FeeTypeId);
        feeIds.AddRange(roomMeters.SelectMany(g => g));
        var feeTypes = await db.FeeTypes.AsNoTracking().Include(f => f.Prices)
            .Where(f => feeIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var issues = new List<DataReviewIssue>();
        foreach (var c in contracts)
        {
            var (property, room) = codes[c.Id];
            DataReviewIssue Issue(string code, string message, Guid? renterId = null) => new(
                c.Id, c.ContractNo, property, room, code, message, renterId, renterId is { } id ? people.GetValueOrDefault(id).Name : null);

            // Quan hệ người ở theo hồ sơ hiện tại (người còn ở hoặc sắp vào).
            var staying = c.Occupants.Where(o => o.MoveOutDate is null || o.MoveOutDate >= today).ToList();
            if (people.TryGetValue(c.ReferenceRenterId, out var reference))
            {
                foreach (var v in OccupantRelationshipRules.Check(reference.Facts,
                             staying.Where(o => people.ContainsKey(o.RenterId)).Select(o => (o.ToInput(), people[o.RenterId].Facts)).ToList()))
                {
                    var renterId = staying.Where(o => people.ContainsKey(o.RenterId)).ElementAt(v.Index).RenterId;
                    issues.Add(Issue(v.Error.Code, v.Error.Message, renterId));
                }
            }

            if (c.SignedDate is { } signed && people.TryGetValue(c.RepresentativeRenterId, out var rep)
                && rep.Facts.DateOfBirth.AgeOn(signed) < LessorDetails.MinimumAge)
                issues.Add(Issue(ContractErrors.RepresentativeUnderage.Code,
                    "Theo hồ sơ hiện tại, người đứng tên chưa đủ 18 tuổi tại ngày ký — kiểm tra lại ngày sinh.", c.RepresentativeRenterId));

            var unpriced = c.Fees.Where(f => f.Covers(today) && f.UnitPriceOverride is null)
                .Select(f => feeTypes.GetValueOrDefault(f.FeeTypeId)).OfType<FeeType>()
                .Concat(roomMeters[c.RoomId].Select(id => feeTypes.GetValueOrDefault(id)).OfType<FeeType>())
                .Where(t => t.ResolvePrice(today) is null)
                .DistinctBy(t => t.Id);
            foreach (var type in unpriced)
                issues.Add(Issue("FEE_PRICE_MISSING", $"Khoản thu \"{type.Name}\" chưa có giá — thêm bảng giá trước khi lập phiếu."));
        }

        issues.AddRange(LivingInTwoRooms(contracts, codes, people.ToDictionary(p => p.Key, p => p.Value.Name), today));
        return issues.OrderBy(i => i.PropertyCode).ThenBy(i => i.RoomCode).ThenBy(i => i.Code).ToList();
    }

    private async Task<Dictionary<Guid, (string Property, string Room)>> LoadCodesAsync(List<Contract> contracts, CancellationToken ct)
    {
        var roomIds = contracts.Select(c => c.RoomId).Distinct().ToList();
        var rooms = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id))
            .Join(db.Properties, r => r.PropertyId, p => p.Id, (r, p) => new { r.Id, Room = r.Code, Property = p.Code })
            .ToDictionaryAsync(x => x.Id, ct);
        return contracts.ToDictionary(c => c.Id, c => (rooms[c.RoomId].Property, rooms[c.RoomId].Room));
    }

    /// <summary>CT-BR-31 rà lại trên dữ liệu đã lưu (dữ liệu cũ trước khi có kiểm tra, hoặc nhập lùi ngày).</summary>
    private static IEnumerable<DataReviewIssue> LivingInTwoRooms(
        List<Contract> contracts, Dictionary<Guid, (string Property, string Room)> codes, Dictionary<Guid, string> names, DateOnly today)
    {
        var stays = contracts.SelectMany(c => c.Occupants
                .Where(o => o.MoveOutDate is null || o.MoveOutDate >= today)
                .Select(o => (Contract: c, o.RenterId, From: o.MoveInDate, To: o.MoveOutDate ?? c.ActualEndDate)))
            .GroupBy(s => s.RenterId)
            .Where(g => g.Select(s => s.Contract.RoomId).Distinct().Count() > 1);

        foreach (var group in stays)
        {
            var list = group.OrderBy(s => s.From).ToList();
            for (var i = 1; i < list.Count; i++)
            {
                var (prev, cur) = (list[i - 1], list[i]);
                if (prev.Contract.RoomId != cur.Contract.RoomId && (prev.To is null || prev.To > cur.From))
                {
                    var (property, room) = codes[cur.Contract.Id];
                    yield return new DataReviewIssue(cur.Contract.Id, cur.Contract.ContractNo, property, room,
                        ContractErrors.OccupantLivesElsewhere.Code,
                        $"Đang đồng thời ở phòng {codes[prev.Contract.Id].Room} (hợp đồng {prev.Contract.ContractNo}) — ghi chuyển đi ở một nơi.",
                        group.Key, names.GetValueOrDefault(group.Key));
                }
            }
        }
    }
}

using FluentValidation;
using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Application.Contracts;
using renting_room.Application.Renters;

namespace renting_room.Application.Rooms;

// ============================================================ RT-UC-04: lịch sử thuê theo phòng, tìm theo tên người

public enum TenancyRole
{
    Representative,
    CoTenant,
    Resident
}

/// <param name="FromDate">Ngày người này bắt đầu ở phòng trong lần ở đó.</param>
/// <param name="ToDate">Ngày cuối ở phòng; null = đang ở.</param>
public sealed record TenancyOccupantDto(Guid RenterId, string FullName, TenancyRole Role, DateOnly FromDate, DateOnly? ToDate);

/// <summary>Một người đứng tên HĐ trong một lần ở phòng (đổi người đứng tên giữa chừng ⇒ nhiều dòng).</summary>
public sealed record TenancyRepresentativeDto(Guid RenterId, string FullName, DateOnly FromDate, DateOnly? ToDate);

/// <summary>Một lần một HĐ ở phòng (HĐ chuyển đi rồi quay lại ⇒ 2 dòng).</summary>
/// <param name="RepresentativeName">Người đứng tên cuối lần ở đó.</param>
public sealed record RoomTenancyDto(
    Guid ContractId, string ContractNo, ContractStatus Status, DateOnly FromDate, DateOnly? ToDate, bool IsCurrent, string RepresentativeName,
    IReadOnlyList<TenancyOccupantDto> Occupants, IReadOnlyList<TenancyRepresentativeDto> Representatives);

public sealed record PersonTenancyDto(
    Guid RenterId, string FullName, Guid RoomId, string RoomCode, Guid ContractId, string ContractNo, TenancyRole Role, DateOnly FromDate,
    DateOnly? ToDate);

public sealed record GetRoomTenancyHistoryQuery(Guid RoomId) : IRequest<Result<IReadOnlyList<RoomTenancyDto>>>;

/// <param name="Search">Một phần họ tên (không phân biệt dấu / hoa thường), tối thiểu 2 ký tự.</param>
public sealed record SearchTenancyHistoryQuery(string Search, Guid? PropertyId) : IRequest<IReadOnlyList<PersonTenancyDto>>;

public sealed class SearchTenancyHistoryQueryValidator : AbstractValidator<SearchTenancyHistoryQuery>
{
    public SearchTenancyHistoryQueryValidator() =>
        RuleFor(x => x.Search).Must(s => s is not null && s.Trim().Length >= 2)
            .WithErrorCode("SEARCH_TOO_SHORT").WithMessage("Nhập ít nhất 2 ký tự của họ tên.");
}

internal static class TenancyHistoryRules
{
    /// <summary>Ai đứng tên HĐ theo thời gian (CT-BR-51): những người đứng tên trước, rồi người hiện tại.</summary>
    public static IEnumerable<(Guid RenterId, DateOnly From, DateOnly? To)> Representatives(Contract contract) =>
        contract.RepresentativeHistory.OrderBy(h => h.FromDate)
            .Select(h => (h.RenterId, h.FromDate, (DateOnly?)(h.ToDate > h.FromDate ? h.ToDate.AddDays(-1) : h.ToDate)))
            .Append((contract.RepresentativeRenterId, contract.RepresentativeSince ?? contract.StartDate, contract.ActualEndDate));

    /// <summary>Những người đứng tên trong lần ở phòng đó, kèm khoảng ngày đứng tên nằm trong lần ở.</summary>
    public static IEnumerable<(Guid RenterId, DateOnly From, DateOnly? To)> RepresentativesIn(Contract contract, RoomStay stay) =>
        from representative in Representatives(contract)
        let period = Within(stay, representative.From, representative.To)
        where period is not null
        select (representative.RenterId, period.Value.From, period.Value.To);

    /// <summary>Vai trò trong lần ở đó: "đứng tên" nếu người này từng đứng tên trong lần ở (không lấy người đứng tên hiện tại cho giai đoạn cũ).</summary>
    public static TenancyRole RoleOf(Contract contract, RoomStay stay, ContractOccupant occupant) =>
        RepresentativesIn(contract, stay).Any(r => r.RenterId == occupant.RenterId) ? TenancyRole.Representative
        : occupant.Role == OccupantRole.CoTenant ? TenancyRole.CoTenant
        : TenancyRole.Resident;

    /// <summary>Phần khoảng [from, to] nằm trong lần ở phòng; null nếu không giao nhau.</summary>
    public static (DateOnly From, DateOnly? To)? Within(RoomStay stay, DateOnly from, DateOnly? to)
    {
        var start = from > stay.From ? from : stay.From;
        var end = stay.To is { } a && to is { } b ? (a < b ? a : b) : stay.To ?? to;
        return end is { } last && last < start ? null : (start, end);
    }

    /// <summary>Các người ở của HĐ có mặt trong lần ở phòng đó, kèm khoảng ngày thực ở.</summary>
    public static IEnumerable<(ContractOccupant Occupant, DateOnly From, DateOnly? To)> OccupantsIn(Contract contract, RoomStay stay) =>
        from occupant in contract.Occupants
        let period = Within(stay, occupant.MoveInDate, occupant.MoveOutDate ?? contract.ActualEndDate)
        where period is not null
        select (occupant, period.Value.From, period.Value.To);
}

/// <summary>RT-UC-04: ai đã / đang ở phòng này — theo từng lần ở của từng HĐ (tính cả lịch sử chuyển phòng), mới nhất trước.</summary>
public sealed class GetRoomTenancyHistoryHandler(IRoomQueries rooms, IContractQueries contractQueries, IRenterQueries renters, TimeProvider clock)
    : IRequestHandler<GetRoomTenancyHistoryQuery, Result<IReadOnlyList<RoomTenancyDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RoomTenancyDto>>> Handle(GetRoomTenancyHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await rooms.ExistsAsync(request.RoomId, cancellationToken))
            return PropertyErrors.RoomNotFound;

        var contracts = await contractQueries.ListLivedInRoomAsync(request.RoomId, cancellationToken);
        var renterIds = contracts
            .SelectMany(c => c.Occupants.Select(o => o.RenterId).Append(c.RepresentativeRenterId).Concat(c.RepresentativeHistory.Select(h => h.RenterId)))
            .Distinct().ToList();
        var names = await renters.GetNamesAsync(renterIds, cancellationToken);
        var today = clock.GetUtcNow().ToBusinessDate();

        return contracts
            .SelectMany(c => c.RoomStays.Where(s => s.RoomId == request.RoomId).Select(stay => new RoomTenancyDto(
                c.Id, c.ContractNo, c.Status, stay.From, stay.To, stay.From <= today && (stay.To is null || stay.To >= today),
                names.GetValueOrDefault(
                    TenancyHistoryRules.RepresentativesIn(c, stay).Select(r => (Guid?)r.RenterId).LastOrDefault() ?? c.RepresentativeRenterId, string.Empty),
                TenancyHistoryRules.OccupantsIn(c, stay).OrderBy(x => x.From)
                    .Select(x => new TenancyOccupantDto(x.Occupant.RenterId, names.GetValueOrDefault(x.Occupant.RenterId, string.Empty),
                        TenancyHistoryRules.RoleOf(c, stay, x.Occupant), x.From, x.To))
                    .ToList(),
                TenancyHistoryRules.RepresentativesIn(c, stay)
                    .Select(r => new TenancyRepresentativeDto(r.RenterId, names.GetValueOrDefault(r.RenterId, string.Empty), r.From, r.To))
                    .ToList())))
            .OrderByDescending(t => t.FromDate)
            .ToList();
    }
}

/// <summary>RT-UC-04: tìm theo tên — người đó đã ở (hoặc đứng tên) phòng nào, HĐ nào, từ ngày nào tới ngày nào.</summary>
public sealed class SearchTenancyHistoryHandler(IRenterQueries renters, IContractQueries contractQueries, IRoomQueries rooms)
    : IRequestHandler<SearchTenancyHistoryQuery, IReadOnlyList<PersonTenancyDto>>
{
    private const int MaxPeople = 50;
    private const int MaxRows = 200;

    public async ValueTask<IReadOnlyList<PersonTenancyDto>> Handle(SearchTenancyHistoryQuery request, CancellationToken cancellationToken)
    {
        var text = TextNormalizer.ToSearchText(request.Search);
        var people = await renters.SearchNamesAsync(text, MaxPeople, cancellationToken);
        if (people.Count == 0)
            return [];

        var ids = people.Keys.ToList();
        var contracts = await contractQueries.ListLivedByRentersAsync(ids, request.PropertyId, cancellationToken);
        var roomIds = contracts.SelectMany(c => c.RoomStays.Select(s => s.RoomId)).Distinct().ToList();
        var roomCodes = await rooms.GetCodesAsync(roomIds, cancellationToken);

        var rows = new List<PersonTenancyDto>();
        foreach (var contract in contracts)
            foreach (var stay in contract.RoomStays)
            {
                var roomCode = roomCodes.GetValueOrDefault(stay.RoomId, string.Empty);
                rows.AddRange(TenancyHistoryRules.OccupantsIn(contract, stay).Where(x => people.ContainsKey(x.Occupant.RenterId))
                    .Select(x => new PersonTenancyDto(x.Occupant.RenterId, people[x.Occupant.RenterId], stay.RoomId, roomCode, contract.Id,
                        contract.ContractNo, TenancyHistoryRules.RoleOf(contract, stay, x.Occupant), x.From, x.To)));
                // Người đứng tên không ở trong phòng (VD bố mẹ ký cho con) vẫn là người thuê của phòng.
                foreach (var representative in TenancyHistoryRules.RepresentativesIn(contract, stay))
                    if (people.TryGetValue(representative.RenterId, out var name) && contract.Occupants.All(o => o.RenterId != representative.RenterId))
                        rows.Add(new PersonTenancyDto(representative.RenterId, name, stay.RoomId, roomCode, contract.Id, contract.ContractNo,
                            TenancyRole.Representative, representative.From, representative.To));
            }
        return rows.OrderBy(r => r.FullName).ThenByDescending(r => r.FromDate).Take(MaxRows).ToList();
    }
}

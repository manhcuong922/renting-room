using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Models;
using renting_room.Application.Contracts;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Infrastructure.Persistence.Queries;

internal sealed class ContractQueries(AppDbContext db) : IContractQueries
{
    private static readonly ContractStatus[] LivedStatuses = [ContractStatus.Active, ContractStatus.Liquidating, ContractStatus.Ended];

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>>> GetFlagsAsync(
        IEnumerable<Guid> contractIds, DateOnly today, CancellationToken cancellationToken) =>
        LoadFlagsAsync(db, contractIds, today, cancellationToken);

    /// <summary>
    /// CT-BR-44/45: cờ của nhiều HĐ cho danh sách HĐ / danh sách phòng — nạp HĐ đang hiệu lực kèm người ở rồi dùng đúng
    /// <see cref="Contract.Flags"/> của domain (một nguồn quy tắc duy nhất, không viết lại bằng SQL).
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>>> LoadFlagsAsync(
        AppDbContext db, IEnumerable<Guid> contractIds, DateOnly today, CancellationToken ct)
    {
        var ids = contractIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ContractFlag>>();

        var contracts = await db.Contracts.AsNoTracking().Include(c => c.Occupants)
            .Where(c => ids.Contains(c.Id) && c.Status == ContractStatus.Active)
            .ToListAsync(ct);
        return contracts.ToDictionary(c => c.Id, c => c.Flags(today));
    }

    public async Task<IReadOnlyList<Contract>> ListLivedInRoomAsync(Guid roomId, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Include(c => c.Occupants).Include(c => c.RoomMoves).Include(c => c.RepresentativeHistory).AsSplitQuery()
            .Where(c => LivedStatuses.Contains(c.Status)
                && (c.RoomId == roomId || c.RoomMoves.Any(m => m.RoomId == roomId)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Contract>> ListLivedByRentersAsync(
        IReadOnlyCollection<Guid> renterIds, Guid? propertyId, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Include(c => c.Occupants).Include(c => c.RoomMoves).Include(c => c.RepresentativeHistory).AsSplitQuery()
            .Where(c => LivedStatuses.Contains(c.Status)
                && (propertyId == null || c.PropertyId == propertyId)
                && (renterIds.Contains(c.RepresentativeRenterId) || c.Occupants.Any(o => renterIds.Contains(o.RenterId))
                    || c.RepresentativeHistory.Any(h => renterIds.Contains(h.RenterId))))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Contract>> ListForBillingTransitionAsync(
        Guid propertyId, DateOnly from, DateOnly until, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Include(c => c.RentTerms)
            .Where(c => c.PropertyId == propertyId && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && c.BillingStartDate <= until && (c.ActualEndDate == null || c.ActualEndDate >= from))
            .ToListAsync(cancellationToken);

    public Task<Contract?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<ContractStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.Where(c => c.Id == id).Select(c => (ContractStatus?)c.Status).FirstOrDefaultAsync(cancellationToken);

    public Task<ContractHeader?> GetHeaderAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new ContractHeader(c.Id, c.ContractNo, c.Status)).FirstOrDefaultAsync(cancellationToken);

    public Task<Contract?> GetWithRentTermsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.AsNoTracking().Include(c => c.RentTerms).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Contract>> ListWithRentTermsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Include(c => c.RentTerms).Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

    public Task<Contract?> GetWithRoomMovesAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.AsNoTracking().Include(c => c.RoomMoves).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BillingCandidate>> ListBillingCandidatesAsync(
        IReadOnlyCollection<Guid> roomIds, DateOnly month, DateOnly monthEnd, Guid? contractId, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking()
            .Where(c => roomIds.Contains(c.RoomId)
                && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.BillingStartDate <= monthEnd.AddMonths(1) && (c.ActualEndDate == null || c.ActualEndDate >= month))
            .Where(c => contractId == null || c.Id == contractId)
            .OrderBy(c => c.ContractNo)
            .Select(c => new BillingCandidate(c.Id, c.RoomId, c.ContractNo))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Contract>> ListForMeterSheetAsync(
        IReadOnlyCollection<Guid> roomIds, DateOnly monthEnd, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Include(c => c.RentTerms).Include(c => c.Occupants).Include(c => c.RoomMoves)
            .AsSplitQuery()
            .Where(c => roomIds.Contains(c.RoomId)
                && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && c.BillingStartDate <= monthEnd.AddMonths(1))
            .ToListAsync(cancellationToken);

    public Task<ContractTenancy?> FindTenancyAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken) =>
        db.Contracts.AsNoTracking()
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && ((c.RoomId == roomId && c.RoomSince <= date && (c.ActualEndDate == null || c.ActualEndDate >= date))
                    || c.RoomMoves.Any(m => m.RoomId == roomId && m.FromDate <= date && m.ToDate >= date)))
            .OrderByDescending(c => c.StartDate).Select(c => new ContractTenancy(c.Id, c.Status)).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> AnyActiveInRoomAsync(Guid roomId, CancellationToken cancellationToken) =>
        db.Contracts.AnyAsync(c => c.RoomId == roomId
            && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating), cancellationToken);

    public Task<Guid?> GetPreviousContractIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.Where(c => c.Id == id).Select(c => c.PreviousContractId).FirstOrDefaultAsync(cancellationToken);

    public Task<ContractChainLink?> GetChainLinkAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.Where(c => c.Id == id).Select(c => new ContractChainLink(c.ContractNo, c.PreviousContractId)).FirstOrDefaultAsync(cancellationToken);

    public Task<Contract?> GetDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.AsNoTracking()
            .Include(c => c.RentTerms).Include(c => c.Occupants).Include(c => c.Assets).Include(c => c.Vehicles).Include(c => c.Fees)
            .Include(c => c.RoomMoves).Include(c => c.RepresentativeHistory)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<PagedResult<ContractSummaryDto>> ListAsync(ListContractsQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var query = db.Contracts.AsNoTracking();

        if (request.PropertyId is { } propertyId)
            query = query.Where(c => c.PropertyId == propertyId);
        if (request.RoomId is { } roomId)
            query = query.Where(c => c.RoomId == roomId);
        if (request.RenterId is { } renterId)
            query = query.Where(c => c.RepresentativeRenterId == renterId || c.Occupants.Any(o => o.RenterId == renterId));
        if (request.Status is { } status)
            query = query.Where(c => c.Status == status);
        if (request.ExpiringWithinDays is { } days)
        {
            var until = today.AddDays(days);
            query = query.Where(c => c.Status == ContractStatus.Active && c.EndDate >= today && c.EndDate <= until);
        }
        if (request.Overdue == true)
            query = query.Where(c => c.Status == ContractStatus.Active && c.EndDate < today);
        // CT-BR-27: nhóm hợp đồng không cọc / có cọc.
        // Thiếu tài liệu: HĐ nháp / đang ở / đang thanh lý chưa có bản ký (giấy / ảnh / PDF).
        if (request.MissingSignedDocument is { } missing)
            query = query.Where(c => (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && c.HasSignedDocument != missing);
        if (request.HasDeposit is { } hasDeposit)
            query = hasDeposit ? query.Where(c => c.DepositAmount > 0) : query.Where(c => c.DepositAmount == 0);
        // M08 PM-BR-35: HĐ đã kết thúc có cọc mà chưa đánh dấu hoàn trả.
        if (request.DepositNotRefunded == true)
            query = query.Where(c => c.Status == ContractStatus.Ended && c.DepositAmount > 0 && c.DepositStatus == DepositStatus.Holding);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = TextNormalizer.NormalizeCode(request.Search);
            query = query.Where(c => c.ContractNo.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.StartDate).ThenBy(c => c.ContractNo)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new ContractSummaryDto(
                c.Id,
                c.ContractNo,
                c.Status,
                c.PropertyId,
                db.Properties.Where(p => p.Id == c.PropertyId).Select(p => p.Code).First(),
                c.RoomId,
                db.Rooms.Where(r => r.Id == c.RoomId).Select(r => r.Code).First(),
                c.RepresentativeRenterId,
                db.Renters.Where(r => r.Id == c.RepresentativeRenterId).Select(r => r.FullName).First(),
                c.StartDate,
                c.EndDate,
                c.ActualEndDate,
                c.RentTerms.Where(t => t.EffectiveFrom <= today).OrderByDescending(t => t.EffectiveFrom).Select(t => (decimal?)t.MonthlyRent).FirstOrDefault(),
                c.Occupants.Count(o => o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today)),
                c.Status == ContractStatus.Active && c.EndDate < today,
                c.ContractType,
                c.DepositAmount,
                null))
            .ToListAsync(cancellationToken);
        return new PagedResult<ContractSummaryDto>(items, request.Page, request.PageSize, total);
    }

    public Task<Guid?> GetRoomIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contracts.Where(c => c.Id == id).Select(c => (Guid?)c.RoomId).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> IsContractNoTakenAsync(string contractNo, CancellationToken cancellationToken) =>
        db.Contracts.AnyAsync(c => c.ContractNo == contractNo, cancellationToken);

    public Task<bool> AnyRoomMoveFromAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken) =>
        db.Contracts.AnyAsync(c => c.RoomMoves.Any(m => m.RoomId == roomId && m.ToDate >= date), cancellationToken);

    public async Task<bool> IsRoomTakenAsync(Guid exceptContractId, Guid roomId, DateOnly date, CancellationToken cancellationToken) =>
        await db.Contracts.AnyAsync(c => c.Id != exceptContractId && c.RoomId == roomId
            && (c.Status == ContractStatus.Draft
                || ((c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                    && (c.ActualEndDate == null || c.ActualEndDate >= date))), cancellationToken)
        || await db.Contracts.AnyAsync(c => c.Id != exceptContractId && c.RoomMoves.Any(m => m.RoomId == roomId && m.ToDate >= date), cancellationToken);

    public Task<Guid?> GetActiveIdInRoomAsync(Guid roomId, DateOnly today, CancellationToken cancellationToken) =>
        db.Contracts.Where(c => c.RoomId == roomId && c.Status == ContractStatus.Active && c.StartDate <= today)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Contract>> ListActiveWithFeesAsync(Guid propertyId, DateOnly today, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Include(c => c.Fees)
            .Where(c => c.PropertyId == propertyId
                && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && (c.ActualEndDate == null || c.ActualEndDate >= today))
            .ToListAsync(cancellationToken);

    private static readonly ContractStatus[] ReviewStatuses = [ContractStatus.Active, ContractStatus.Liquidating];

    public async Task<IReadOnlyList<Contract>> ListOpenForReviewAsync(Guid? propertyId, CancellationToken cancellationToken)
    {
        var query = db.Contracts.AsNoTracking().Include(c => c.Occupants).Include(c => c.Fees).AsSplitQuery()
            .Where(c => ReviewStatuses.Contains(c.Status));
        if (propertyId is { } id)
            query = query.Where(c => c.PropertyId == id);
        return await query.ToListAsync(cancellationToken);
    }

    public Task<OccupantRef?> FindOccupantAsync(Guid contractId, Guid occupantId, CancellationToken cancellationToken) =>
        db.Contracts.Where(c => c.Id == contractId)
            .Select(c => new OccupantRef(c.Occupants.Where(o => o.Id == occupantId).Select(o => (Guid?)o.RenterId).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<VehicleRow>> ListOpenVehiclesAsync(Guid contractId, Guid renterId, CancellationToken cancellationToken) =>
        await db.Contracts.Where(c => c.Id == contractId).SelectMany(c => c.Vehicles)
            .Where(v => v.RenterId == renterId && v.RegisteredTo == null)
            .Select(v => new VehicleRow(v.Id, v.VehicleType, v.PlateNumber, v.BrandColor, v.Note)).ToListAsync(cancellationToken);

    public Task<bool> IsPlateRegisteredAsync(string plate, CancellationToken cancellationToken) =>
        db.Contracts.SelectMany(c => c.Vehicles).AnyAsync(v => v.PlateNumber == plate && v.RegisteredTo == null, cancellationToken);

    private static readonly ContractStatus[] RunningStatuses = [ContractStatus.Active, ContractStatus.Liquidating];

    public async Task<IReadOnlyList<StayRow>> ListStaysElsewhereAsync(Guid roomId, IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken) =>
        await (
                from c in db.Contracts
                where c.RoomId != roomId && LivedStatuses.Contains(c.Status)
                from o in c.Occupants
                where renterIds.Contains(o.RenterId)
                join room in db.Rooms on c.RoomId equals room.Id
                // Đang thanh lý: ngày chuyển đi của từng người chỉ ghi khi hoàn tất ⇒ dùng ngày trả phòng của HĐ làm hạn cuối.
                select new StayRow(o.RenterId, o.MoveInDate, o.MoveOutDate ?? c.ActualEndDate, c.ContractNo, room.Code))
            .ToListAsync(cancellationToken);

    public async Task<ContractRoomRef?> FindResidentElsewhereAsync(
        Guid contractId, Guid representativeId, DateOnly start, CancellationToken cancellationToken) =>
        await (
                from c in db.Contracts
                where c.Id != contractId && RunningStatuses.Contains(c.Status) && c.RepresentativeRenterId != representativeId
                from o in c.Occupants
                where o.RenterId == representativeId && o.Role == OccupantRole.Resident
                    && ((o.MoveOutDate ?? c.ActualEndDate) == null || (o.MoveOutDate ?? c.ActualEndDate) > start)
                join room in db.Rooms on c.RoomId equals room.Id
                select new ContractRoomRef(c.ContractNo, room.Code))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SignedContractRow>> ListRunningSignedByAsync(
        Guid exceptContractId, IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken) =>
        await db.Contracts.Where(c => c.Id != exceptContractId && RunningStatuses.Contains(c.Status) && renterIds.Contains(c.RepresentativeRenterId))
            .Select(c => new SignedContractRow(c.RepresentativeRenterId, c.ActualEndDate)).ToListAsync(cancellationToken);

    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];

    public async Task<IReadOnlyList<RenterStayRow>> ListOpenStaysAsync(
        IReadOnlyCollection<Guid> renterIds, DateOnly today, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking()
            .Where(c => OpenStatuses.Contains(c.Status))
            .SelectMany(c => c.Occupants.Where(o => renterIds.Contains(o.RenterId) && (o.MoveOutDate == null || o.MoveOutDate >= today))
                .Select(o => new RenterStayRow(o.RenterId, c.ContractNo, db.Rooms.Where(r => r.Id == c.RoomId).Select(r => r.Code).First())))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoomContractRow>> ListOpenInRoomsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Where(c => roomIds.Contains(c.RoomId) && OpenStatuses.Contains(c.Status))
            .Select(c => new RoomContractRow(c.RoomId, c.ContractNo, c.Status,
                db.Renters.Where(r => r.Id == c.RepresentativeRenterId).Select(r => r.FullName).First()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoomOccupiedRow>> ListOccupiedUntilAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken)
    {
        var endedUntil = await db.Contracts.AsNoTracking().Where(c => roomIds.Contains(c.RoomId) && c.Status == ContractStatus.Ended)
            .Select(c => new RoomOccupiedRow(c.RoomId, c.ActualEndDate!.Value)).ToListAsync(cancellationToken);
        var movedUntil = await db.Contracts.AsNoTracking().SelectMany(c => c.RoomMoves).Where(m => roomIds.Contains(m.RoomId))
            .Select(m => new RoomOccupiedRow(m.RoomId, m.ToDate)).ToListAsync(cancellationToken);
        return endedUntil.Concat(movedUntil).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListIdsByFormerRepresentativeAsync(Guid renterId, CancellationToken cancellationToken) =>
        await db.Contracts.Where(c => c.RepresentativeHistory.Any(h => h.RenterId == renterId)).Select(c => c.Id).ToListAsync(cancellationToken);
}

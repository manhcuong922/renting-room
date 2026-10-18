using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Kiểm tra người ở dùng chung cho tạo/sửa nháp, thêm người ở và kích hoạt (CT-BR-28..31).</summary>
internal static class OccupantChecks
{
    /// <summary>RT-BR-01: hồ sơ có số giấy tờ (người đứng tên HĐ bắt buộc có).</summary>
    public static Task<bool> HasIdNumberAsync(IAppDbContext db, Guid renterId, CancellationToken ct) =>
        db.Renters.AnyAsync(r => r.Id == renterId && r.IdNumberHash != null, ct);

    /// <summary>CT-BR-49: mọi người trong danh sách đều có số giấy tờ (đồng thuê có tên trên HĐ).</summary>
    public static async Task<bool> AllHaveIdNumberAsync(IAppDbContext db, IReadOnlyCollection<Guid> renterIds, CancellationToken ct) =>
        renterIds.Count == 0 || await db.Renters.CountAsync(r => renterIds.Contains(r.Id) && r.IdNumberHash != null, ct) == renterIds.Count;

    private static readonly ContractStatus[] LivedStatuses = [ContractStatus.Active, ContractStatus.Liquidating, ContractStatus.Ended];
    private static readonly ContractStatus[] RunningStatuses = [ContractStatus.Active, ContractStatus.Liquidating];

    public static async Task<Dictionary<Guid, (PersonFacts Facts, string Name)>> LoadPeopleAsync(
        IAppDbContext db, IEnumerable<Guid> renterIds, CancellationToken ct)
    {
        var ids = renterIds.Distinct().ToList();
        // Hồ sơ đã ẩn danh (RT-BR-06) coi như không còn — không đứng tên / ở trong HĐ mới được.
        var people = await db.Renters.AsNoTracking().Where(r => ids.Contains(r.Id) && r.AnonymizedAt == null)
            .Select(r => new { r.Id, r.DateOfBirth, r.Gender, r.FullName })
            .ToListAsync(ct);
        return people.ToDictionary(p => p.Id, p => (new PersonFacts(p.Id, p.DateOfBirth, p.Gender), p.FullName));
    }

    /// <summary>Lỗi quan hệ ⇒ 400 theo từng field (<paramref name="pathOf"/> dựng đường dẫn JSON từ vi phạm).</summary>
    public static void ThrowIfInvalid(IEnumerable<OccupantRuleViolation> violations, Func<OccupantRuleViolation, string> pathOf)
    {
        var failures = violations
            .Select(v => new ValidationFailure(pathOf(v), v.Error.Message) { ErrorCode = v.Error.Code })
            .ToList();
        if (failures.Count > 0)
            throw new ValidationException(failures);
    }

    /// <summary>
    /// CT-BR-31: một người không ở 2 phòng cùng lúc. Chuyển đi ngày X và vào phòng mới ngày X là hợp lệ.
    /// Chỉ xét HĐ đã bàn giao (nháp chưa có ai ở) ở <b>phòng khác</b> — trùng HĐ cùng phòng đã do EXCLUDE
    /// <c>ex_contracts_room_period</c> chặn với lỗi rõ hơn (<c>ROOM_PERIOD_OVERLAP</c>).
    /// </summary>
    public static async Task<Error?> FindLivingElsewhereAsync(
        IAppDbContext db, Contract contract, IReadOnlyCollection<(Guid RenterId, DateOnly From, DateOnly? To)> stays,
        IReadOnlyDictionary<Guid, string> names, CancellationToken ct)
    {
        var renterIds = stays.Select(s => s.RenterId).Distinct().ToList();
        // Giữ khóa tới khi commit ⇒ lệnh song song trên hợp đồng khác phải chờ và thấy kết quả của lệnh này.
        await db.LockRentersAsync(renterIds, ct);
        var others = await (
                from c in db.Contracts
                where c.RoomId != contract.RoomId && LivedStatuses.Contains(c.Status)
                from o in c.Occupants
                where renterIds.Contains(o.RenterId)
                join room in db.Rooms on c.RoomId equals room.Id
                // Đang thanh lý: ngày chuyển đi của từng người chỉ ghi khi hoàn tất ⇒ dùng ngày trả phòng của HĐ làm hạn cuối.
                select new { o.RenterId, o.MoveInDate, MoveOutDate = o.MoveOutDate ?? c.ActualEndDate, c.ContractNo, RoomCode = room.Code })
            .ToListAsync(ct);

        foreach (var stay in stays)
        {
            var conflict = others.FirstOrDefault(o => o.RenterId == stay.RenterId
                && (stay.To is null || o.MoveInDate < stay.To)
                && (o.MoveOutDate is null || o.MoveOutDate > stay.From));
            if (conflict is not null)
                return Error.Conflict(ContractErrors.OccupantLivesElsewhere.Code,
                    $"{names.GetValueOrDefault(stay.RenterId, "Người này")} đang ở phòng {conflict.RoomCode} (hợp đồng {conflict.ContractNo}) " +
                    "trong cùng thời gian — ghi nhận chuyển đi ở hợp đồng đó trước.");
        }

        return null;
    }

    /// <summary>
    /// CT-BR-50: người đứng tên không đồng thời là người ở thường (không đứng tên, không đồng thuê) của HĐ khác đang chạy — xét cả hai chiều
    /// trên trạng thái hiện có của <paramref name="contract"/> (gọi sau khi đã đổi người ở / vai trò / người đứng tên trong bộ nhớ).
    /// </summary>
    public static async Task<Error?> FindRoleConflictAsync(IAppDbContext db, Contract contract, CancellationToken ct)
    {
        var (representativeId, contractId, start) = (contract.RepresentativeRenterId, contract.Id, contract.StartDate);
        // Người đứng tên HĐ này đang là người ở thường của HĐ khác.
        var elsewhere = await (
                from c in db.Contracts
                where c.Id != contractId && RunningStatuses.Contains(c.Status) && c.RepresentativeRenterId != representativeId
                from o in c.Occupants
                where o.RenterId == representativeId && o.Role == OccupantRole.Resident
                    && ((o.MoveOutDate ?? c.ActualEndDate) == null || (o.MoveOutDate ?? c.ActualEndDate) > start)
                join room in db.Rooms on c.RoomId equals room.Id
                select new { RenterId = representativeId, c.ContractNo, RoomCode = room.Code })
            .FirstOrDefaultAsync(ct);

        // Người ở thường của HĐ này đang đứng tên một HĐ khác.
        if (elsewhere is null)
        {
            var residents = contract.Occupants
                .Where(o => o.Role == OccupantRole.Resident && o.RenterId != representativeId && o.MoveOutDate is null)
                .ToDictionary(o => o.RenterId, o => o.MoveInDate);
            var residentIds = residents.Keys.ToList();
            var signed = residents.Count == 0
                ? []
                : await db.Contracts.Where(c => c.Id != contractId && RunningStatuses.Contains(c.Status) && residentIds.Contains(c.RepresentativeRenterId))
                    .Select(c => new { c.RepresentativeRenterId, c.ActualEndDate }).ToListAsync(ct);
            // HĐ kia trả phòng đúng ngày người đó vào ở đây thì hợp lệ (như CT-BR-31).
            var signer = signed.FirstOrDefault(c => c.ActualEndDate is null || c.ActualEndDate > residents[c.RepresentativeRenterId])?.RepresentativeRenterId;
            if (signer is { } signerId)
                elsewhere = new
                {
                    RenterId = signerId, contract.ContractNo,
                    RoomCode = await db.Rooms.Where(r => r.Id == contract.RoomId).Select(r => r.Code).FirstAsync(ct)
                };
        }
        if (elsewhere is null)
            return null;

        var name = await db.Renters.Where(r => r.Id == elsewhere.RenterId).Select(r => r.FullName).FirstOrDefaultAsync(ct) ?? "Người này";
        return ContractErrors.RepresentativeIsResidentElsewhere(name, elsewhere.RoomCode, elsewhere.ContractNo);
    }
}

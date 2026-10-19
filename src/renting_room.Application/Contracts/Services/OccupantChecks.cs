using FluentValidation;
using FluentValidation.Results;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Kiểm tra người ở dùng chung cho tạo/sửa nháp, thêm người ở và kích hoạt (CT-BR-28..31).</summary>
public sealed class OccupantChecks(IRenterQueries renters, IContractQueries contracts, IRoomQueries rooms, IUnitOfWork unitOfWork)
{
    /// <summary>RT-BR-01: hồ sơ có số giấy tờ (người đứng tên HĐ bắt buộc có).</summary>
    public Task<bool> HasIdNumberAsync(Guid renterId, CancellationToken ct) => renters.HasIdNumberAsync(renterId, ct);

    /// <summary>CT-BR-49: mọi người trong danh sách đều có số giấy tờ (đồng thuê có tên trên HĐ).</summary>
    public async Task<bool> AllHaveIdNumberAsync(IReadOnlyCollection<Guid> renterIds, CancellationToken ct) =>
        renterIds.Count == 0 || await renters.CountWithIdNumberAsync(renterIds, ct) == renterIds.Count;

    public async Task<Dictionary<Guid, (PersonFacts Facts, string Name)>> LoadPeopleAsync(IEnumerable<Guid> renterIds, CancellationToken ct)
    {
        var ids = renterIds.Distinct().ToList();
        // Hồ sơ đã ẩn danh (RT-BR-06) coi như không còn — không đứng tên / ở trong HĐ mới được.
        var people = await renters.ListPeopleAsync(ids, ct);
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
    public async Task<Error?> FindLivingElsewhereAsync(
        Contract contract, IReadOnlyCollection<(Guid RenterId, DateOnly From, DateOnly? To)> stays,
        IReadOnlyDictionary<Guid, string> names, CancellationToken ct)
    {
        var renterIds = stays.Select(s => s.RenterId).Distinct().ToList();
        // Giữ khóa tới khi commit ⇒ lệnh song song trên hợp đồng khác phải chờ và thấy kết quả của lệnh này.
        await unitOfWork.LockRentersAsync(renterIds, ct);
        var others = await contracts.ListStaysElsewhereAsync(contract.RoomId, renterIds, ct);

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
    public async Task<Error?> FindRoleConflictAsync(Contract contract, CancellationToken ct)
    {
        var (representativeId, contractId, start) = (contract.RepresentativeRenterId, contract.Id, contract.StartDate);
        // Người đứng tên HĐ này đang là người ở thường của HĐ khác.
        var found = await contracts.FindResidentElsewhereAsync(contractId, representativeId, start, ct);
        (Guid RenterId, string ContractNo, string RoomCode)? elsewhere = found is null ? null : (representativeId, found.ContractNo, found.RoomCode);

        // Người ở thường của HĐ này đang đứng tên một HĐ khác.
        if (elsewhere is null)
        {
            var residents = contract.Occupants
                .Where(o => o.Role == OccupantRole.Resident && o.RenterId != representativeId && o.MoveOutDate is null)
                .ToDictionary(o => o.RenterId, o => o.MoveInDate);
            var residentIds = residents.Keys.ToList();
            var signed = residents.Count == 0
                ? []
                : await contracts.ListRunningSignedByAsync(contractId, residentIds, ct);
            // HĐ kia trả phòng đúng ngày người đó vào ở đây thì hợp lệ (như CT-BR-31).
            var signer = signed.FirstOrDefault(c => c.ActualEndDate is null || c.ActualEndDate > residents[c.RepresentativeRenterId])?.RepresentativeRenterId;
            if (signer is { } signerId)
                elsewhere = (signerId, contract.ContractNo, (await rooms.GetCodeAsync(contract.RoomId, ct))!);
        }
        if (elsewhere is not { } conflict)
            return null;

        var name = (await renters.GetNamesAsync([conflict.RenterId], ct)).GetValueOrDefault(conflict.RenterId) ?? "Người này";
        return ContractErrors.RepresentativeIsResidentElsewhere(name, conflict.RoomCode, conflict.ContractNo);
    }
}

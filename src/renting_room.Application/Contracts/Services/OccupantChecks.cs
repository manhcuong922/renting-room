using FluentValidation;
using FluentValidation.Results;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed class OccupantChecks(IRenterQueries renters, IContractQueries contracts, IRoomQueries rooms, IUnitOfWork unitOfWork)
{
    public Task<bool> HasIdNumberAsync(Guid renterId, CancellationToken ct) => renters.HasIdNumberAsync(renterId, ct);

    public async Task<bool> AllHaveIdNumberAsync(IReadOnlyCollection<Guid> renterIds, CancellationToken ct) =>
        renterIds.Count == 0 || await renters.CountWithIdNumberAsync(renterIds, ct) == renterIds.Count;

    public async Task<Dictionary<Guid, (PersonFacts Facts, string Name)>> LoadPeopleAsync(IEnumerable<Guid> renterIds, CancellationToken ct)
    {
        var ids = renterIds.Distinct().ToList();
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

    public async Task<Error?> FindRoleConflictAsync(Contract contract, CancellationToken ct)
    {
        var (representativeId, contractId, start) = (contract.RepresentativeRenterId, contract.Id, contract.StartDate);
        var found = await contracts.FindResidentElsewhereAsync(contractId, representativeId, start, ct);
        (Guid RenterId, string ContractNo, string RoomCode)? elsewhere = found is null ? null : (representativeId, found.ContractNo, found.RoomCode);

        if (elsewhere is null)
        {
            var residents = contract.Occupants
                .Where(o => o.Role == OccupantRole.Resident && o.RenterId != representativeId && o.MoveOutDate is null)
                .ToDictionary(o => o.RenterId, o => o.MoveInDate);
            var residentIds = residents.Keys.ToList();
            var signed = residents.Count == 0
                ? []
                : await contracts.ListRunningSignedByAsync(contractId, residentIds, ct);
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

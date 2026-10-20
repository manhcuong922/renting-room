using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>
/// CT-UC-21 ký lại không thành (hủy nháp ký lại / hủy thanh lý HĐ cũ): mở lại trên HĐ cũ các xe đã kết thúc lúc ký lại. Gọi trong transaction của lệnh
/// hủy, sau khi nháp đã nhả biển số trong bộ nhớ.
/// </summary>
public sealed class ResignVehicleRestorer(IUnitOfWork unitOfWork, IContractQueries contracts)
{
    /// <param name="newContractId">HĐ nháp ký lại bị hủy; null = mọi lần ký lại của HĐ cũ (hủy thanh lý).</param>
    public async Task<IReadOnlyList<Warning>> RestoreAsync(Contract old, Guid? newContractId, CancellationToken ct)
    {
        var candidates = old.VehiclesEndedByResign(newContractId);
        if (candidates.Count == 0)
            return [];

        // Lưu việc nháp nhả biển số trước khi mở lại xe — unique "biển số đang đăng ký" kiểm từng câu lệnh.
        await unitOfWork.SaveChangesAsync(ct);
        var taken = new HashSet<string>();
        foreach (var plate in candidates.Select(v => v.PlateNumber).OfType<string>().Distinct())
            if (await contracts.IsPlateRegisteredAsync(plate, ct))
                taken.Add(plate);

        return old.ReopenResignedVehicles(newContractId, taken).Select(v => ContractWarnings.VehicleNotRestored(v.PlateNumber!)).ToList();
    }
}

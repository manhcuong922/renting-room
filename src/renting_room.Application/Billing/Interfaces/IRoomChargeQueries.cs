using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

public interface IRoomChargeQueries
{
    /// <summary>Khoản phát sinh chưa hủy, chưa lên phiếu của HĐ.</summary>
    Task<int> CountPendingAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Mới nhất trước, tối đa <paramref name="maxRows"/>.</summary>
    Task<IReadOnlyList<RoomCharge>> ListAsync(ListRoomChargesQuery request, int maxRows, CancellationToken cancellationToken);

    Task<Guid?> GetContractIdAsync(Guid chargeId, CancellationToken cancellationToken);

    /// <summary>BL-BR-38: khoản chốt điện nước khi chuyển phòng (gắn công tơ).</summary>
    Task<bool> IsMeterClosingAsync(Guid chargeId, CancellationToken cancellationToken);

    /// <summary>Khoản bù chưa hủy, chưa trả, chưa vào phiếu đã chốt của HĐ.</summary>
    Task<decimal> GetWaitingCreditTotalAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Khoản bù đã đánh dấu trả, chưa hủy.</summary>
    Task<SettledCredit?> FindSettledCreditAsync(Guid chargeId, CancellationToken cancellationToken);
}

public sealed record SettledCredit(Guid ContractId, DateOnly IncurredOn, decimal Amount);

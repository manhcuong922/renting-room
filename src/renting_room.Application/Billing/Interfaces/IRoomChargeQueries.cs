using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

public interface IRoomChargeQueries
{
    Task<int> CountPendingAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Mới nhất trước, tối đa <paramref name="maxRows"/>.</summary>
    Task<IReadOnlyList<RoomCharge>> ListAsync(ListRoomChargesQuery request, int maxRows, CancellationToken cancellationToken);

    Task<Guid?> GetContractIdAsync(Guid chargeId, CancellationToken cancellationToken);

    Task<bool> IsSystemAsync(Guid chargeId, CancellationToken cancellationToken);

    Task<decimal> GetWaitingCreditTotalAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Khoản bù đã trả ngay — trừ khoản điều chỉnh chỉ số (không áp giới hạn bù, M07 BL-BR-40).</summary>
    Task<SettledCredit?> FindSettledCreditAsync(Guid chargeId, CancellationToken cancellationToken);
}

public sealed record SettledCredit(Guid ContractId, DateOnly IncurredOn, decimal Amount);

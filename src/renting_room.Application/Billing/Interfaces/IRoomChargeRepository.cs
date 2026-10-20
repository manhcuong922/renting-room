using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

public interface IRoomChargeRepository
{
    Task<RoomCharge?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomCharge>> ListPendingByContractAsync(Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomCharge>> ListByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomCharge>> ListTransferChargesAsync(
        Guid contractId, Guid oldRoomId, DateOnly date, Guid? rentChargeId, CancellationToken cancellationToken);

    /// <summary>Khoản điều chỉnh chỉ số chưa hủy của dòng nguồn (hóa đơn, khoản thu, ngày đầu kỳ sử dụng).</summary>
    Task<RoomCharge?> FindReadingAdjustmentForUpdateAsync(Guid sourceInvoiceId, Guid feeTypeId, DateOnly usageStart, CancellationToken cancellationToken);

    /// <summary>Khoản điều chỉnh chỉ số chưa hủy trỏ tới hóa đơn nguồn này.</summary>
    Task<IReadOnlyList<RoomCharge>> ListReadingAdjustmentsAsync(Guid sourceInvoiceId, CancellationToken cancellationToken);

    void Add(RoomCharge charge);
}

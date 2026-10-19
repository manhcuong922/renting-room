using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

/// <summary>Lấy / cất khoản phát sinh của phòng.</summary>
public interface IRoomChargeRepository
{
    Task<RoomCharge?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Khoản chưa hủy, chưa lên phiếu của HĐ — theo ngày phát sinh rồi ngày tạo.</summary>
    Task<IReadOnlyList<RoomCharge>> ListPendingByContractAsync(Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomCharge>> ListByInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Khoản chưa hủy của lần chuyển phòng: khoản chốt điện nước của phòng cũ tại ngày chuyển (dữ liệu cũ) hoặc khoản chênh lệch tiền phòng.
    /// </summary>
    Task<IReadOnlyList<RoomCharge>> ListTransferChargesAsync(
        Guid contractId, Guid oldRoomId, DateOnly date, Guid? rentChargeId, CancellationToken cancellationToken);

    void Add(RoomCharge charge);
}

using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

/// <summary>Lấy / cất aggregate phiếu. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Invoice"/>.</summary>
public interface IInvoiceRepository
{
    /// <summary>Phiếu kèm dòng và đoạn đo.</summary>
    Task<Invoice?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Phiếu chưa hủy của HĐ, kèm dòng và đoạn đo.</summary>
    Task<IReadOnlyList<Invoice>> ListWithDetailsByContractAsync(Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Invoice>> ListByContractsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    /// <summary>Phiếu của <paramref name="contractIds"/> (trừ <paramref name="excludedContractIds"/>) chốt tên người đứng tên này.</summary>
    Task<IReadOnlyList<Invoice>> ListByRepresentativeNameAsync(
        IReadOnlyCollection<Guid> contractIds, IReadOnlyCollection<Guid> excludedContractIds, string representativeName, CancellationToken cancellationToken);

    /// <summary>Khóa (C-07, theo thứ tự id) rồi lấy các phiếu đã chốt còn nợ của HĐ. Phải gọi trong transaction.</summary>
    Task<IReadOnlyList<Invoice>> LockOpenForUpdateAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Khóa (theo thứ tự id) rồi lấy các phiếu. Phải gọi trong transaction.</summary>
    Task<IReadOnlyDictionary<Guid, Invoice>> LockForUpdateAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    /// <summary>Phiếu quyết toán (chưa hủy) của HĐ — không kèm dòng.</summary>
    Task<Invoice?> FindFinalForUpdateAsync(Guid contractId, CancellationToken cancellationToken);

    void Add(Invoice invoice);

    void Remove(Invoice invoice);
}

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

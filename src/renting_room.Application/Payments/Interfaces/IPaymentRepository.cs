using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

/// <summary>Lấy / cất phiếu thu. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Payment"/>.</summary>
public interface IPaymentRepository
{
    Task<IReadOnlyList<Payment>> ListByContractsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    Task<Payment?> GetWithAllocationsForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Phiếu loại <paramref name="kind"/> chưa đảo có phân bổ (chưa hủy) vào hóa đơn này, kèm phân bổ — mới nhất trước (ngày thu, lúc tạo).</summary>
    Task<IReadOnlyList<Payment>> ListActiveForInvoiceAsync(Guid invoiceId, PaymentKind kind, CancellationToken cancellationToken);

    void Add(Payment payment);

    /// <summary>M08 PM-BR-38 (8): lịch sử giảm số đã thu.</summary>
    void AddReduction(CollectionReduction reduction);
}

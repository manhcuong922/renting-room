using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

/// <summary>Lấy / cất phiếu thu. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Payment"/>.</summary>
public interface IPaymentRepository
{
    Task<IReadOnlyList<Payment>> ListByContractsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    Task<Payment?> GetWithAllocationsForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Phiếu bỏ nợ chưa đảo có phân bổ (chưa hủy) vào hóa đơn này, kèm phân bổ — mới nhất trước.</summary>
    Task<IReadOnlyList<Payment>> ListActiveWriteOffsForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    void Add(Payment payment);
}

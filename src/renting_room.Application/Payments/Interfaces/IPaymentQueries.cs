using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

public interface IPaymentQueries
{
    /// <summary>Phiếu thu (chỉ đọc, kèm phân bổ), mới nhất trước, tối đa <paramref name="maxRows"/>.</summary>
    Task<IReadOnlyList<Payment>> ListAsync(ListPaymentsQuery request, int maxRows, CancellationToken cancellationToken);

    Task<Guid?> GetContractIdAsync(Guid paymentId, CancellationToken cancellationToken);
}

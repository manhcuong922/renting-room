using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

/// <summary>Lấy / cất aggregate phiếu. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Invoice"/>.</summary>
public interface IInvoiceRepository
{
    Task<Invoice?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Invoice>> ListWithDetailsByContractAsync(Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Invoice>> ListByContractsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    /// <summary>Phiếu của <paramref name="contractIds"/> (trừ <paramref name="excludedContractIds"/>) chốt tên người đứng tên này.</summary>
    Task<IReadOnlyList<Invoice>> ListByRepresentativeNameAsync(
        IReadOnlyCollection<Guid> contractIds, IReadOnlyCollection<Guid> excludedContractIds, string representativeName, CancellationToken cancellationToken);

    /// <summary>Khóa (C-07, theo thứ tự id) rồi lấy các phiếu đã chốt còn nợ của HĐ. Phải gọi trong transaction.</summary>
    Task<IReadOnlyList<Invoice>> LockOpenForUpdateAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Khóa (theo thứ tự id) rồi lấy các phiếu. Phải gọi trong transaction.</summary>
    Task<IReadOnlyDictionary<Guid, Invoice>> LockForUpdateAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    Task<Invoice?> FindFinalForUpdateAsync(Guid contractId, CancellationToken cancellationToken);

    void Add(Invoice invoice);

    void AddAdjustment(InvoiceAdjustment adjustment);

    void Remove(Invoice invoice);

    Task MarkDraftsStaleAsync(DraftStaleScope scope, CancellationToken cancellationToken);
}

using renting_room.Domain.Billing;

namespace renting_room.Application.Billing;

/// <summary>Khoảng ngắt tính tiền của HĐ (M07 BL-BR-42). Không chứa luật nghiệp vụ.</summary>
public interface IBillingGapRepository
{
    Task<IReadOnlyList<ContractBillingGap>> ListByContractAsync(Guid contractId, CancellationToken cancellationToken);

    Task<ContractBillingGap?> FindResumedByAsync(Guid invoiceId, CancellationToken cancellationToken);

    void Add(ContractBillingGap gap);

    void Remove(ContractBillingGap gap);
}

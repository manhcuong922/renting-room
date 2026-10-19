using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Lấy / cất aggregate hợp đồng (kèm các phần con cần cho use case). Không chứa luật nghiệp vụ.</summary>
public interface IContractRepository
{
    /// <summary>HĐ kèm đủ phần con (giá thuê, người ở, tài sản, xe, khoản thu, chuyển phòng, lịch sử đứng tên).</summary>
    Task<Contract?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>HĐ (không kèm phần con).</summary>
    Task<Contract?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    void Add(Contract contract);

    /// <summary>HĐ người này đứng tên hoặc có xe đăng ký tên người này; kèm xe.</summary>
    Task<IReadOnlyList<Contract>> ListWithVehiclesByRenterAsync(Guid renterId, CancellationToken cancellationToken);
}

/// <summary>Lấy / cất mẫu hợp đồng.</summary>
public interface IContractTemplateRepository
{
    Task<ContractTemplate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    void Add(ContractTemplate template);
}

public interface IContractTemplateQueries
{
    /// <summary>Chỉ đọc.</summary>
    Task<ContractTemplate?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Theo loại rồi tên.</summary>
    Task<IReadOnlyList<ContractTemplate>> ListAsync(ContractType? type, bool includeArchived, CancellationToken cancellationToken);
}

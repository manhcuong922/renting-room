using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

/// <summary>Lấy / cất aggregate khoản thu (gồm bảng giá). Không chứa luật nghiệp vụ — luật nằm trong <see cref="FeeType"/>.</summary>
public interface IFeeTypeRepository
{
    Task<FeeType?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Khóa hàng khoản thu tới hết transaction rồi nạp — tuần tự hóa các lệnh sửa bảng giá. Phải gọi trong transaction.</summary>
    Task<FeeType?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeeType>> ListActiveByPropertyAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<bool> IsNameTakenAsync(Guid propertyId, string normalizedName, string? systemCode, Guid? excludeId, CancellationToken cancellationToken);

    void Add(FeeType fee);
}

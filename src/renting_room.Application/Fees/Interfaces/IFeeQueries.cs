using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

public sealed record FeeName(Guid Id, string Name);

public interface IFeeQueries
{
    Task<FeeTypeDto?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeeTypeDto>> ListAsync(Guid propertyId, bool includeArchived, DateOnly today, CancellationToken cancellationToken);

    /// <summary>HĐ nháp / hiệu lực / thanh lý còn gắn khoản thu (bản đăng ký chưa kết thúc tính tới hôm nay).</summary>
    Task<bool> IsUsedByOpenContractsAsync(Guid feeTypeId, DateOnly today, CancellationToken cancellationToken);

    Task<bool> HasActiveMetersAsync(Guid feeTypeId, CancellationToken cancellationToken);

    /// <summary>Khoản thu (chỉ đọc, không kèm giá).</summary>
    Task<FeeType?> FindTypeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Khoản thu (chỉ đọc) kèm bảng giá.</summary>
    Task<FeeType?> GetWithPricesAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, FeeType>> GetTypesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, FeeType>> GetTypesWithPricesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Khoản "tự gắn" (không phải điện nước theo công tơ) chưa ngừng dùng của khu, theo thứ tự hiển thị (FE-UC-01).</summary>
    Task<IReadOnlyList<FeeType>> ListAutoAttachAsync(Guid propertyId, CancellationToken cancellationToken);

    /// <summary>Khoản có mã hệ thống (điện / nước) chưa ngừng dùng của khu: mã → id.</summary>
    Task<Dictionary<string, Guid>> GetSystemFeeIdsAsync(Guid propertyId, CancellationToken cancellationToken);

    /// <summary>Khoản Điện chưa ngừng dùng của khu.</summary>
    Task<Guid?> FindElectricityIdAsync(Guid propertyId, CancellationToken cancellationToken);

    /// <summary>Tên các khoản thu, theo thứ tự hiển thị.</summary>
    Task<IReadOnlyList<FeeName>> ListNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Khoản phí giữ xe (có loại xe) của khu (CT-BR-22).</summary>
    Task<IReadOnlyCollection<FeeType>> ListParkingFeesAsync(Guid propertyId, CancellationToken cancellationToken);
}

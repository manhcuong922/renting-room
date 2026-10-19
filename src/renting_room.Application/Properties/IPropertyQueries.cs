using renting_room.Application.Common.Models;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

/// <summary>Đọc thông tin khu (chỉ trong tổ chức hiện tại).</summary>
public interface IPropertyQueries
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Số khu trong <paramref name="ids"/> có trong tổ chức.</summary>
    Task<int> CountAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<string?> GetCodeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>null ⇒ không có khu này.</summary>
    Task<PropertyStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PropertyStatus>> GetStatusesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Khu (chỉ đọc) để dựng DTO / tính toán — không dùng để sửa.</summary>
    Task<Property?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<PropertySummaryDto>> ListAsync(ListPropertiesQuery request, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Còn HĐ nháp / hiệu lực / thanh lý, hoặc HĐ đã kết thúc mà hôm nay vẫn là ngày trả phòng.</summary>
    Task<bool> HasOpenContractsAsync(Guid propertyId, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Ngày cuối kỳ muộn nhất của phiếu (chưa hủy) của khu; null = khu chưa có phiếu.</summary>
    Task<DateOnly?> GetLastInvoicePeriodEndAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<bool> HasDraftInvoicesAsync(Guid propertyId, CancellationToken cancellationToken);

    /// <summary>Cài đặt kỳ thu của khu (PR-BR-09) — mọi HĐ của khu dùng chung.</summary>
    Task<PropertyBilling> GetBillingAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, PropertyBilling>> GetBillingsAsync(IEnumerable<Guid> propertyIds, CancellationToken cancellationToken);

    /// <summary>Mã các khu trong <paramref name="ids"/> (trống = mọi khu đang dùng), theo mã.</summary>
    Task<IReadOnlyList<PropertyCodeRow>> ListCodesAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken);
}

public sealed record PropertyCodeRow(Guid Id, string Code);

public sealed record PropertyStatus(Guid Id, bool IsArchived);

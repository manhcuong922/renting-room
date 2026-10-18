namespace renting_room.Application.Common.Interfaces;

/// <summary>Cấp số hợp đồng tự động <c>HD{yyyy}-{0000}</c> theo tổ chức/năm (CT-BR-15) — an toàn khi gọi song song.</summary>
public interface IContractNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, int year, CancellationToken cancellationToken);
}

/// <summary>Cấp số chứng từ <c>{prefix}{yyyy}-{000000}</c> theo tổ chức/năm: <c>PB</c> phiếu báo (BL-BR-13), <c>PT</c> phiếu thu (PM-BR-15).</summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, string prefix, int year, CancellationToken cancellationToken);
}

/// <summary>
/// Kỳ đầu tiên chưa có phiếu đã chốt của hợp đồng (CT-BR-05) — dùng cho đổi giá thuê / dịch vụ (CT-UC-05). Mỗi phiếu thu tiền phòng đúng
/// kỳ của nó (không còn chu kỳ nhiều tháng) ⇒ một mốc chung cho cả giá thuê và dịch vụ.
/// </summary>
public interface IInvoiceLockReader
{
    Task<DateOnly?> GetFirstOpenPeriodStartAsync(Guid contractId, CancellationToken cancellationToken);
}

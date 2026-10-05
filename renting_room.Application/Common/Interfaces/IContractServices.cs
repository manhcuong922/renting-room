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
/// Kỳ đầu tiên chưa có phiếu đã chốt của hợp đồng (CT-BR-05). Hiện thực thật ở M07 (Billing);
/// trước khi có M07 trả null = chưa kỳ nào bị khóa.
/// </summary>
public interface IInvoiceLockReader
{
    Task<DateOnly?> GetFirstOpenPeriodStartAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>CT-BR-05 / BL-BR-26: kỳ đầu tiên chưa bị khóa cho đổi giá thuê — sau cả chu kỳ tiền phòng đã chốt (chu kỳ nhiều tháng thu trước).</summary>
    Task<DateOnly?> GetFirstOpenRentPeriodStartAsync(Guid contractId, CancellationToken cancellationToken);
}

namespace renting_room.Application.Common.Interfaces;

/// <summary>Cấp số hợp đồng tự động <c>HD{yyyy}-{0000}</c> theo tổ chức/năm (CT-BR-15) — an toàn khi gọi song song.</summary>
public interface IContractNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, int year, CancellationToken cancellationToken);
}

/// <summary>
/// Kỳ đầu tiên chưa có phiếu đã chốt của hợp đồng (CT-BR-05). Hiện thực thật ở M07 (Billing);
/// trước khi có M07 trả null = chưa kỳ nào bị khóa.
/// </summary>
public interface IInvoiceLockReader
{
    Task<DateOnly?> GetFirstOpenPeriodStartAsync(Guid contractId, CancellationToken cancellationToken);
}

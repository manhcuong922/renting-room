namespace renting_room.Application.Common.Interfaces;

/// <summary>Cấp số hợp đồng tự động <c>HD{yyyy}-{0000}</c> theo tổ chức/năm (CT-BR-15) — an toàn khi gọi song song.</summary>
public interface IContractNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, int year, CancellationToken cancellationToken);
}

public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, string prefix, int year, CancellationToken cancellationToken);
}

public interface IInvoiceLockReader
{
    Task<DateOnly?> GetFirstOpenPeriodStartAsync(Guid contractId, CancellationToken cancellationToken);
}

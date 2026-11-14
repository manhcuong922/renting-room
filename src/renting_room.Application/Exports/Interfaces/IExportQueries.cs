namespace renting_room.Application.Exports;

public interface IExportQueries
{
    Task<int> CountRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<RenterExportRow>> ListRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>M10 E2: số hóa đơn khớp bộ lọc (tháng thu trong [from, to]).</summary>
    Task<int> CountInvoicesAsync(ExportMonthlyInvoicesQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<InvoiceExportData> LoadInvoicesAsync(ExportMonthlyInvoicesQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

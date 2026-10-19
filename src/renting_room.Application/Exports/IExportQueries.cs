namespace renting_room.Application.Exports;

public interface IExportQueries
{
    /// <summary>E1: số dòng người ở có thời gian ở giao với [from, to] trong HĐ đã bàn giao, theo bộ lọc.</summary>
    Task<int> CountRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<RenterExportRow>> ListRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

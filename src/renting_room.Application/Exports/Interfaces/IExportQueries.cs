namespace renting_room.Application.Exports;

public interface IExportQueries
{
    Task<int> CountRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<RenterExportRow>> ListRentersAsync(ExportRentersQuery request, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

using Microsoft.Extensions.Diagnostics.HealthChecks;
using renting_room.Application.Attachments;

namespace renting_room.Http;

/// <summary>M09 FS-BR-14: kho tệp lỗi ⇒ <c>Degraded</c> (app vẫn phục vụ các chức năng khác), không phải <c>Unhealthy</c>.</summary>
public sealed class StorageHealthCheck(IFileStorageSelector storages) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storages.ForNewFiles.CheckAvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (StorageUnavailableException)
        {
            return HealthCheckResult.Degraded("File storage is unavailable.");
        }
    }
}

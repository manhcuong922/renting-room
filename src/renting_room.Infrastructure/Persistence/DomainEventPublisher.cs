using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Infrastructure.Persistence;

/// <summary>
/// Gom sự kiện của các entity đang theo dõi trước khi lưu, phát cho các <see cref="IDomainEventHandler"/> sau khi lưu thành công.
/// Handler lấy lúc phát (không qua constructor) vì chúng dùng repository — tức chính DbContext này.
/// </summary>
public sealed class DomainEventPublisher(IServiceProvider services)
{
    public static PendingDomainEvents Collect(ChangeTracker tracker)
    {
        var sources = tracker.Entries<Entity>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        if (sources.Count == 0)
            return PendingDomainEvents.None;
        var organizationId = sources.OfType<ITenantEntity>().Select(e => e.OrganizationId).FirstOrDefault();
        return new PendingDomainEvents(sources, new DomainEventBatch(organizationId, sources.SelectMany(e => e.DomainEvents).ToList()));
    }

    public async Task PublishAsync(PendingDomainEvents pending, CancellationToken cancellationToken)
    {
        if (pending.Batch is not { } batch)
            return;
        // Đã lưu ⇒ sự kiện đã xảy ra; xóa trước khi phát để lần lưu sau không phát lại.
        foreach (var source in pending.Sources)
            source.ClearDomainEvents();
        foreach (var handler in services.GetServices<IDomainEventHandler>())
            await handler.HandleAsync(batch, cancellationToken);
    }
}

public sealed record PendingDomainEvents(IReadOnlyList<Entity> Sources, DomainEventBatch? Batch)
{
    public static readonly PendingDomainEvents None = new([], null);
}

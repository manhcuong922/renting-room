using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

public sealed class DraftStalenessHandler(IInvoiceRepository invoices) : IDomainEventHandler
{
    public async Task HandleAsync(DomainEventBatch batch, CancellationToken cancellationToken)
    {
        if (DraftStaleScope.From(batch) is { } scope)
            await invoices.MarkDraftsStaleAsync(scope, cancellationToken);
    }
}

public sealed record DraftStaleScope(
    Guid OrganizationId, IReadOnlyCollection<Guid> ContractIds, IReadOnlyCollection<Guid> RoomIds, IReadOnlyCollection<Guid> MeterIds,
    IReadOnlyCollection<Guid> FeeTypeIds, DateOnly PriceFrom)
{
    public static DraftStaleScope? From(DomainEventBatch batch)
    {
        var contracts = new HashSet<Guid>();
        var rooms = new HashSet<Guid>();
        var meters = new HashSet<Guid>();
        var feeTypes = new HashSet<Guid>();
        var priceFrom = DateOnly.MaxValue;
        // Khoản thu vừa tạo (kèm giá ban đầu) chưa HĐ nào dùng ⇒ không ảnh hưởng nháp nào.
        var created = batch.Events.OfType<FeeTypeCreated>().Select(e => e.FeeTypeId).ToHashSet();

        foreach (var domainEvent in batch.Events)
        {
            switch (domainEvent)
            {
                case ContractBillingChanged e:
                    contracts.Add(e.ContractId);
                    break;
                case MeterChanged e:
                    rooms.Add(e.RoomId);
                    break;
                case MeterReadingChanged e:
                    meters.Add(e.MeterId);
                    break;
                case FeePriceChanged e when !created.Contains(e.FeeTypeId):
                    feeTypes.Add(e.FeeTypeId);
                    priceFrom = e.EffectiveFrom < priceFrom ? e.EffectiveFrom : priceFrom;
                    break;
                case FeeTypeChanged e when !created.Contains(e.FeeTypeId):
                    feeTypes.Add(e.FeeTypeId);
                    priceFrom = DateOnly.MinValue;
                    break;
            }
        }

        var empty = contracts.Count + rooms.Count + meters.Count + feeTypes.Count == 0;
        return empty || batch.OrganizationId == Guid.Empty ? null : new DraftStaleScope(batch.OrganizationId, contracts, rooms, meters, feeTypes, priceFrom);
    }
}

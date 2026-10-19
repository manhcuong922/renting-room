using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

/// <summary>
/// BL-BR-20 (I1, rút gọn 10/10/2026): nháp đã lập mà giá dịch vụ / điện nước, chỉ số công tơ hoặc giá thuê / người ở / dịch vụ của HĐ đổi
/// ⇒ cờ "Cần tính lại" (xe, cài đặt khu: không — ít ảnh hưởng, tránh báo thừa). Chỉ là gợi ý: chốt vẫn tính lại và so (BL-BR-12).
/// </summary>
public sealed class DraftStalenessHandler(IInvoiceRepository invoices) : IDomainEventHandler
{
    public async Task HandleAsync(DomainEventBatch batch, CancellationToken cancellationToken)
    {
        if (DraftStaleScope.From(batch) is { } scope)
            await invoices.MarkDraftsStaleAsync(scope, cancellationToken);
    }
}

/// <summary>Nháp cần đánh dấu: của các HĐ, các phòng (có công tơ đổi), và của các khu có khoản thu đổi giá từ <see cref="PriceFrom"/>.</summary>
/// <param name="MeterIds">Công tơ có chỉ số đổi — nháp của phòng đặt công tơ.</param>
/// <param name="PriceFrom">Giá đổi chỉ ảnh hưởng nháp có kỳ kết thúc từ ngày hiệu lực trở đi (giá theo phiên bản tới ngày cuối kỳ — FE-BR-10).</param>
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

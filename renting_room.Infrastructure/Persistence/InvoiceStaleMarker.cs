using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Infrastructure.Persistence;

/// <summary>
/// BL-BR-20 (I1, rút gọn 10/10/2026): nháp đã lập mà giá dịch vụ / điện nước, chỉ số công tơ hoặc giá thuê / người ở / dịch vụ của HĐ đổi
/// ⇒ cờ "Cần tính lại" (xe, cài đặt khu: không — ít ảnh hưởng, tránh báo thừa). Đọc ChangeTracker trước khi lưu (không cần nhớ gọi ở từng lệnh),
/// đánh dấu sau khi lưu bằng 1 câu UPDATE — trong transaction của lệnh nếu có. Chỉ là gợi ý: chốt vẫn tính lại và so (BL-BR-12).
/// </summary>
internal sealed class InvoiceStaleMarker
{
    /// <summary>Đổi các trường này của HĐ mới ảnh hưởng tiền (kỳ, ngày trả phòng); ghi chú, bản ký, giữ cọc… thì không.</summary>
    private static readonly HashSet<string> ContractBillingProperties =
        [nameof(Contract.StartDate), nameof(Contract.ActualEndDate), nameof(Contract.BillingStartDate), nameof(Contract.RoomId), nameof(Contract.RoomSince)];

    private readonly HashSet<Guid> _contracts = [];
    private readonly HashSet<Guid> _rooms = [];
    private readonly HashSet<Guid> _meters = [];
    private readonly HashSet<Guid> _feeTypes = [];
    private Guid _organizationId;

    /// <summary>Giá đổi chỉ ảnh hưởng nháp có kỳ kết thúc từ ngày hiệu lực trở đi (giá theo phiên bản tới ngày cuối kỳ — FE-BR-10).</summary>
    private DateOnly _priceFrom = DateOnly.MaxValue;
    private readonly HashSet<Guid> _newFeeTypes = [];

    private bool IsEmpty => _contracts.Count + _rooms.Count + _meters.Count + _feeTypes.Count == 0;

    public static InvoiceStaleMarker? Collect(ChangeTracker tracker)
    {
        var marker = new InvoiceStaleMarker();
        // Khoản thu vừa tạo (kèm giá ban đầu) chưa HĐ nào dùng ⇒ không ảnh hưởng nháp nào.
        marker._newFeeTypes.UnionWith(tracker.Entries<FeeType>().Where(e => e.State == EntityState.Added).Select(e => e.Entity.Id));
        foreach (var entry in tracker.Entries<TenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            if (marker.Add(entry))
                marker._organizationId = entry.Entity.OrganizationId;
        }
        return marker.IsEmpty || marker._organizationId == Guid.Empty ? null : marker;
    }

    private bool Add(EntityEntry<TenantEntity> entry)
    {
        var (sources, id) = entry.Entity switch
        {
            MeterReading reading => (_meters, reading.MeterId),
            Meter meter => (_rooms, meter.RoomId),
            Contract contract when Changed(entry, ContractBillingProperties) => (_contracts, contract.Id),
            ContractRentTerm term => (_contracts, term.ContractId),
            ContractOccupant occupant => (_contracts, occupant.ContractId),
            ContractFee fee => (_contracts, fee.ContractId),
            ContractRoomMove move => (_contracts, move.ContractId),
            FeePrice price when !_newFeeTypes.Contains(price.FeeTypeId) => (_feeTypes, PriceChanged(price.FeeTypeId, price.EffectiveFrom)),
            FeeType type when entry.State == EntityState.Modified => (_feeTypes, PriceChanged(type.Id, DateOnly.MinValue)),
            _ => (null, Guid.Empty)
        };
        if (sources is null)
            return false;
        sources.Add(id);
        return true;
    }

    private Guid PriceChanged(Guid feeTypeId, DateOnly from)
    {
        if (from < _priceFrom)
            _priceFrom = from;
        return feeTypeId;
    }

    private static bool Changed(EntityEntry entry, HashSet<string> properties) =>
        entry.State != EntityState.Modified || entry.Properties.Any(p => p.IsModified && properties.Contains(p.Metadata.Name));

    public Task MarkAsync(DbContext db, CancellationToken ct) => db.Database.ExecuteSqlAsync(Sql(), ct);

    public void Mark(DbContext db) => db.Database.ExecuteSql(Sql());

    /// <summary>Bảng invoices / meters / fee_types không qua global filter trong SQL thô ⇒ luôn kèm điều kiện tổ chức.</summary>
    private FormattableString Sql()
    {
        var contracts = _contracts.ToArray();
        var rooms = _rooms.ToArray();
        var meters = _meters.ToArray();
        var feeTypes = _feeTypes.ToArray();
        return $"""
            UPDATE invoices SET is_stale = TRUE
            WHERE organization_id = {_organizationId} AND status = 'Draft' AND NOT is_stale
              AND (contract_id = ANY({contracts})
                OR room_id = ANY({rooms})
                OR room_id IN (SELECT m.room_id FROM meters m WHERE m.organization_id = {_organizationId} AND m.id = ANY({meters}))
                OR (period_end >= {_priceFrom}
                    AND property_id IN (SELECT f.property_id FROM fee_types f WHERE f.organization_id = {_organizationId} AND f.id = ANY({feeTypes}))))
            """;
    }
}

using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

/// <summary>Phần dùng chung của ẩn danh tự động và bằng tay: điều kiện chặn + thao tác xóa trên hồ sơ, HĐ, phiếu, phiếu thu, audit.</summary>
public sealed class RenterAnonymizer(IAppDbContext db, IAuditTrail auditTrail, IAuditLogEraser auditLogEraser, TimeProvider clock)
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];

    public static Task<bool> HasOpenContractAsync(IAppDbContext db, Guid renterId, CancellationToken ct) =>
        db.Contracts.AnyAsync(c => (c.RepresentativeRenterId == renterId || c.Occupants.Any(o => o.RenterId == renterId)
                || c.RepresentativeHistory.Any(h => h.RenterId == renterId))
            && OpenStatuses.Contains(c.Status), ct);

    public static Task<bool> HasUnsettledInvoiceAsync(IAppDbContext db, Guid renterId, CancellationToken ct) =>
        db.Invoices.AnyAsync(i => i.Status == InvoiceStatus.Finalized
            && db.Contracts.Any(c => c.Id == i.ContractId && c.RepresentativeRenterId == renterId)
            && (i.PaidAmount < i.TotalAmount || (i.TotalAmount < 0 && i.RefundedOn == null)), ct);

    /// <summary>Lọc bỏ hồ sơ còn HĐ đang chạy hoặc còn phiếu nợ / chờ hoàn (cùng điều kiện với 2 hàm trên, dạng truy vấn tập hợp).</summary>
    public static IQueryable<Renter> WithoutBlockers(IAppDbContext db, IQueryable<Renter> renters) => renters
        .Where(r => !db.Contracts.Any(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id) || c.RepresentativeHistory.Any(h => h.RenterId == r.Id))
            && OpenStatuses.Contains(c.Status)))
        .Where(r => !db.Invoices.Any(i => i.Status == InvoiceStatus.Finalized
            && db.Contracts.Any(c => c.Id == i.ContractId && c.RepresentativeRenterId == r.Id)
            && (i.PaidAmount < i.TotalAmount || (i.TotalAmount < 0 && i.RefundedOn == null))));

    /// <param name="reason">Ẩn danh bằng tay: lý do (ghi audit); null = job tự động.</param>
    public async Task AnonymizeAsync(Guid renterId, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Renter>(renterId, ct);
        var renter = await db.Renters.FirstAsync(r => r.Id == renterId, ct);
        if (renter.IsAnonymized)
            return;

        // RT-BR-06: hết thời gian giữ (job) ⇒ giữ họ tên; người thuê yêu cầu xóa (bằng tay) ⇒ xóa cả tên ở hồ sơ, phiếu, phiếu thu.
        var keepName = reason is null;
        var oldName = renter.FullName;
        var name = keepName ? oldName : Renter.AnonymizedName(renterId);
        var contracts = await db.Contracts.Include(c => c.Vehicles)
            .Where(c => c.RepresentativeRenterId == renterId || c.Vehicles.Any(v => v.RenterId == renterId)).ToListAsync(ct);
        var vehicleIds = contracts.SelectMany(c => c.EraseEndedVehicleIdentity(c.RepresentativeRenterId == renterId ? null : renterId)).ToList();
        contracts = contracts.Where(c => c.RepresentativeRenterId == renterId).ToList();
        var contractIds = contracts.Select(c => c.Id).ToList();
        var invoices = await db.Invoices.Where(i => contractIds.Contains(i.ContractId)).ToListAsync(ct);
        var payments = await db.Payments.Where(p => contractIds.Contains(p.ContractId)).ToListAsync(ct);
        // CT-BR-51: phiếu chốt thời người này còn đứng tên (HĐ sau đó đã đổi người đứng tên).
        var formerContractIds = await db.Contracts.Where(c => c.RepresentativeHistory.Any(h => h.RenterId == renterId)).Select(c => c.Id).ToListAsync(ct);
        invoices.AddRange(await db.Invoices
            .Where(i => formerContractIds.Contains(i.ContractId) && !contractIds.Contains(i.ContractId) && i.SnapshotRepresentativeName == oldName)
            .ToListAsync(ct));

        renter.Anonymize(clock.GetUtcNow(), keepName);
        foreach (var contract in contracts)
            contract.ReplaceSigningSnapshot(SigningSnapshot.FromJson(contract.SigningSnapshot)?.WithAnonymizedRepresentative(name).ToJson());
        if (!keepName)
        {
            foreach (var invoice in invoices)
                invoice.AnonymizeRepresentative(name);
            foreach (var payment in payments)
                payment.ClearPayerName();
        }
        auditTrail.Record(AuditActions.Anonymized, nameof(Renter), renterId,
            new
            {
                mode = reason is null ? "Automatic" : "Manual", reason, contracts = contracts.Count, invoices = invoices.Count, payments = payments.Count,
                vehicles = vehicleIds.Count
            });

        await db.SaveChangesAsync(ct);
        // Sau SaveChanges: chính lần lưu này cũng sinh audit "Updated" chứa giá trị cũ ⇒ xóa cùng lúc, trong cùng transaction.
        await auditLogEraser.EraseRenterAsync(renterId, invoices.Select(i => i.Id).ToList(), payments.Select(p => p.Id).ToList(), vehicleIds, ct);
        await transaction.CommitAsync(ct);
    }
}

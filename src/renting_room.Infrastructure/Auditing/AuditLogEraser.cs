using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Auditing;

/// <summary>
/// RT-BR-06: xóa dữ liệu cá nhân của người được ẩn danh trong <c>audit_logs</c>. Bảng không có global filter ⇒ luôn kèm điều kiện tổ chức.
/// Chạy trên DbContext của lệnh gọi ⇒ dùng chung transaction đang mở.
/// </summary>
internal sealed class AuditLogEraser(AppDbContext db, ICurrentUser currentUser) : IAuditLogEraser
{
    public async Task EraseRenterAsync(
        Guid renterId, IReadOnlyCollection<Guid> invoiceIds, IReadOnlyCollection<Guid> paymentIds, IReadOnlyCollection<Guid> vehicleIds,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new InvalidOperationException("Erasing audit data requires an organization context.");
        var invoices = invoiceIds.ToArray();
        var payments = paymentIds.ToArray();
        var vehicles = vehicleIds.ToArray();

        await db.Database.ExecuteSqlAsync($"""
            UPDATE audit_logs SET changes = NULL
            WHERE organization_id = {organizationId} AND entity_type = 'Renter' AND entity_id = {renterId} AND action <> 'Anonymized'
            """, cancellationToken);
        if (invoices.Length > 0)
            await db.Database.ExecuteSqlAsync($"""
                UPDATE audit_logs SET changes = changes - 'snapshotRepresentativeName'
                WHERE organization_id = {organizationId} AND entity_type = 'Invoice' AND entity_id = ANY({invoices})
                """, cancellationToken);
        if (payments.Length > 0)
            await db.Database.ExecuteSqlAsync($"""
                UPDATE audit_logs SET changes = changes - 'payerName'
                WHERE organization_id = {organizationId} AND entity_type = 'Payment' AND entity_id = ANY({payments})
                """, cancellationToken);
        if (vehicles.Length > 0)
            await db.Database.ExecuteSqlAsync($"""
                UPDATE audit_logs SET changes = changes - 'plateNumber' - 'brandColor'
                WHERE organization_id = {organizationId} AND entity_type = 'ContractVehicle' AND entity_id = ANY({vehicles})
                """, cancellationToken);
    }
}

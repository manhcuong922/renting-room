using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;

namespace renting_room.Infrastructure.Persistence;

/// <summary>FE-BR-07: ngày cuối của dòng phiếu đã chốt có dùng khoản thu — không thêm / xóa giá hồi tố trước ngày đó.</summary>
public sealed class FeePriceLockReader(AppDbContext db) : IFeePriceLockReader
{
    public async Task<DateOnly?> GetLockedUntilAsync(Guid feeTypeId, CancellationToken cancellationToken) =>
        await db.Invoices.Where(i => i.Status == InvoiceStatus.Finalized)
            .SelectMany(i => i.Lines)
            .Where(l => l.FeeTypeId == feeTypeId)
            .MaxAsync(l => (DateOnly?)l.ServiceTo, cancellationToken);
}

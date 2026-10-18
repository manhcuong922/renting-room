using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

// Đánh dấu đã thanh toán / bỏ đánh dấu / hủy
internal static class RoomChargeMutation
{
    /// <summary>
    /// Khóa HĐ → phiếu đang chứa khoản, chặn khi phiếu đã chốt (BL-BR-31/35), chạy thao tác trên khoản + nháp, lưu.
    /// </summary>
    public static async Task<Result<RoomChargeDto>> RunAsync(
        IAppDbContext db, Guid chargeId, Func<RoomCharge, Invoice?, Result> action, CancellationToken ct)
    {
        var contractId = await db.RoomCharges.Where(c => c.Id == chargeId).Select(c => (Guid?)c.ContractId).FirstOrDefaultAsync(ct);
        if (contractId is null)
            return RoomChargeErrors.NotFound;

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId.Value, ct);
        var charge = await db.RoomCharges.FirstAsync(c => c.Id == chargeId, ct);
        Invoice? draft = null;
        if (charge.InvoiceId is { } invoiceId)
        {
            await db.LockForUpdateAsync<Invoice>(invoiceId, ct);
            draft = await InvoiceAccess.LoadAsync(db, invoiceId, ct);
            if (draft is not { Status: InvoiceStatus.Draft })
                return RoomChargeErrors.Locked;
        }

        var result = action(charge, draft);
        if (result.IsFailure)
            return result.Error!;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await RoomChargeBilling.ToDtosAsync(db, [charge], ct))[0];
    }
}

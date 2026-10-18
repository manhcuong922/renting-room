using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

// Sửa nháp: sửa tay, phụ thu / giảm tay, ghi chú
/// <summary>Dòng hệ thống ⇒ bỏ sửa tay (về số hệ thống); phụ thu / giảm tay ⇒ xóa dòng.</summary>
public sealed record ResetInvoiceLineCommand(Guid InvoiceId, Guid LineId) : IRequest<Result<InvoiceDetailDto>>;

public sealed class ResetInvoiceLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ResetInvoiceLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(ResetInvoiceLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), async invoice =>
        {
            if (await EditInvoiceLineHandler.IsSystemChargeLineAsync(db, invoice, request.LineId, cancellationToken))
                return Result.Failure(RoomChargeErrors.System);
            var chargeId = invoice.Lines.FirstOrDefault(l => l.Id == request.LineId)?.RoomChargeId;
            var reset = invoice.ResetLine(request.LineId);
            // BL-BR-34: xóa dòng phụ thu / giảm trừ trên nháp = hủy khoản phát sinh của phòng.
            if (reset.IsSuccess && chargeId is { } id)
                (await db.RoomCharges.FirstAsync(c => c.Id == id, cancellationToken)).Cancel("Xóa dòng trên phiếu nháp", clock.GetUtcNow());
            return reset;
        }, cancellationToken);
}

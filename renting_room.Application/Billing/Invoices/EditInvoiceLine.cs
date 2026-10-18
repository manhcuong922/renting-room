using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

// Sửa nháp: sửa tay, phụ thu / giảm tay, ghi chú
/// <param name="Amount">Bỏ trống ⇒ số lượng × đơn giá × hệ số prorate.</param>
public sealed record EditInvoiceLineCommand(Guid InvoiceId, Guid LineId, decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? Note)
    : IRequest<Result<InvoiceDetailDto>>;

public sealed class EditInvoiceLineCommandValidator : AbstractValidator<EditInvoiceLineCommand>
{
    public EditInvoiceLineCommandValidator()
    {
        RuleFor(x => x.Quantity).Must(q => q is null || (q >= 0 && q <= 99_999_999 && decimal.Round(q.Value, 2) == q))
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Số lượng ≥ 0, tối đa 2 số lẻ.");
        RuleFor(x => x.UnitPrice).Must(p => p is null || (p >= 0 && p <= 1_000_000_000 && decimal.Round(p.Value, 2) == p))
            .WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Amount).OptionalMoney().Must(a => a is null || a <= 1_000_000_000).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Note).OptionalText(300);
    }
}

public sealed class EditInvoiceLineHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<EditInvoiceLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(EditInvoiceLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), async invoice =>
        {
            if (await IsSystemChargeLineAsync(db, invoice, request.LineId, cancellationToken))
                return Result.Failure(RoomChargeErrors.System);
            var edited = invoice.EditLine(request.LineId, request.Quantity, request.UnitPrice, request.Amount, request.Note);
            if (edited.IsFailure)
                return edited;
            var line = invoice.Lines.First(l => l.Id == request.LineId);
            // BL-BR-27: sửa số tiền hoàn trả cũng không vượt số đã thu của phiếu nguồn.
            if (line is { Type: InvoiceLineType.Refund, SourceInvoiceId: { } sourceId } && sourceId != invoice.Id)
            {
                var allowed = await RefundSource.CheckAsync(db, invoice, sourceId, Math.Abs(line.Amount), cancellationToken, line.Id);
                if (allowed.IsFailure)
                    return allowed;
            }
            // BL-BR-34: sửa dòng phụ thu / giảm trừ trên nháp ⇒ sửa khoản phát sinh của phòng.
            if (line.RoomChargeId is { } chargeId)
                (await db.RoomCharges.FirstAsync(c => c.Id == chargeId, cancellationToken)).Update(Math.Abs(line.Amount), line.Note!);
            return edited;
        }, cancellationToken);

    /// <summary>BL-BR-38: dòng của khoản chốt điện nước khi chuyển phòng — không sửa / xóa tay.</summary>
    internal static async Task<bool> IsSystemChargeLineAsync(IAppDbContext db, Invoice invoice, Guid lineId, CancellationToken ct) =>
        invoice.Lines.FirstOrDefault(l => l.Id == lineId)?.RoomChargeId is { } chargeId
        && await db.RoomCharges.AnyAsync(c => c.Id == chargeId && c.MeterId != null, ct);
}

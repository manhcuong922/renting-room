using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

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

public sealed class EditInvoiceLineHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IRoomChargeRepository roomCharges,
    IRoomChargeQueries roomChargeQueries, TimeProvider clock)
    : IRequestHandler<EditInvoiceLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(EditInvoiceLineCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(unitOfWork, invoices, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), async invoice =>
        {
            if (await IsSystemChargeLineAsync(roomChargeQueries, invoice, request.LineId, cancellationToken))
                return Result.Failure(RoomChargeErrors.System);
            var edited = invoice.EditLine(request.LineId, request.Quantity, request.UnitPrice, request.Amount, request.Note);
            if (edited.IsFailure)
                return edited;
            var line = invoice.Lines.First(l => l.Id == request.LineId);
            if (line is { Type: InvoiceLineType.Refund, SourceInvoiceId: { } sourceId } && sourceId != invoice.Id)
            {
                var allowed = await RefundSource.CheckAsync(invoiceQueries, invoice, sourceId, Math.Abs(line.Amount), cancellationToken, line.Id);
                if (allowed.IsFailure)
                    return allowed;
            }
            if (line.RoomChargeId is { } chargeId)
                (await roomCharges.GetForUpdateAsync(chargeId, cancellationToken))!.Update(Math.Abs(line.Amount), line.Note!);
            return edited;
        }, cancellationToken);

    internal static async Task<bool> IsSystemChargeLineAsync(IRoomChargeQueries roomCharges, Invoice invoice, Guid lineId, CancellationToken ct) =>
        invoice.Lines.FirstOrDefault(l => l.Id == lineId)?.RoomChargeId is { } chargeId
        && await roomCharges.IsSystemAsync(chargeId, ct);
}

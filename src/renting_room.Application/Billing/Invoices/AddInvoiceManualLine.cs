using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

/// <param name="Type"><c>Surcharge</c> (phụ thu), <c>ManualDiscount</c> (giảm tay) hoặc <c>Refund</c> (hoàn trả) — nhập số dương.</param>
/// <param name="SourceInvoiceId">Bắt buộc với hoàn trả (BL-BR-27): phiếu đã chốt bị tính sai mà người thuê đã trả.</param>
/// <param name="Settlement">Phụ thu / giảm trừ đã thanh toán / đã hoàn ngay (BL-BR-34) ⇒ dòng hiện trên phiếu, không tính vào tổng.</param>
public sealed record AddInvoiceManualLineCommand(
    Guid InvoiceId, InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    Guid? SourceInvoiceId = null, ChargeSettlement? Settlement = null)
    : IRequest<Result<InvoiceDetailDto>>;

public sealed class AddInvoiceManualLineCommandValidator : AbstractValidator<AddInvoiceManualLineCommand>
{
    public AddInvoiceManualLineCommandValidator()
    {
        ManualLineRules.Apply(this, x => x.Type, x => x.Description, x => x.Note, x => x.Amount, x => x.Quantity, x => x.UnitPrice);
        RuleFor(x => x.SourceInvoiceId).NotNull().When(x => x.Type == InvoiceLineType.Refund)
            .WithErrorCode(BillingErrors.RefundSourceRequired.Code).WithMessage(BillingErrors.RefundSourceRequired.Message);
    }
}

/// <summary>BL-BR-23: phụ thu (sửa chữa do người thuê làm hỏng, lắp thêm, đền bù, tiền điện công tơ cũ khi thay công tơ…) / giảm tay.</summary>
public sealed class AddInvoiceManualLineHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, RoomChargeBilling roomCharges, TimeProvider clock)
    : IRequestHandler<AddInvoiceManualLineCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(AddInvoiceManualLineCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var id in new[] { request.InvoiceId, request.SourceInvoiceId ?? request.InvoiceId }.Distinct().Order())
            await unitOfWork.LockForUpdateAsync<Invoice>(id, cancellationToken);
        var invoice = await invoices.GetForUpdateAsync(request.InvoiceId, cancellationToken);
        if (invoice is null)
            return BillingErrors.NotFound;
        if (request.Type == InvoiceLineType.Refund && request.SourceInvoiceId is { } sourceId && sourceId != invoice.Id)
        {
            var allowed = await RefundSource.CheckAsync(invoiceQueries, invoice, sourceId, request.Amount, cancellationToken);
            if (allowed.IsFailure)
                return allowed.Error!;
        }

        var today = clock.GetUtcNow().ToBusinessDate();
        var added = roomCharges.AddManualLine(invoice, request.Type, request.Description, request.Quantity, request.UnitPrice,
            request.Amount, request.Note, request.FeeTypeId, request.SourceInvoiceId, request.Settlement, today);
        if (added.IsFailure)
            return added.Error!;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.ToDetail(today);
    }
}

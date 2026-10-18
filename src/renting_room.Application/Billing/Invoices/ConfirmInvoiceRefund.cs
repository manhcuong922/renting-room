using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

// Xác nhận đã hoàn tiền (phiếu tổng âm)
public sealed record ConfirmInvoiceRefundCommand(Guid Id, DateOnly RefundedOn, PaymentMethod Method, string? Note) : IRequest<Result<InvoiceDetailDto>>;

public sealed class ConfirmInvoiceRefundCommandValidator : AbstractValidator<ConfirmInvoiceRefundCommand>
{
    public ConfirmInvoiceRefundCommandValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

/// <summary>BL-BR-27: chủ trọ xác nhận đã trả lại người thuê phần tổng âm của phiếu đã chốt (ngày, cách hoàn, ghi chú).</summary>
public sealed class ConfirmInvoiceRefundHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ConfirmInvoiceRefundCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(ConfirmInvoiceRefundCommand request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        return await RefundMutation.RunAsync(db, request.Id, today,
            invoice => invoice.ConfirmRefund(request.RefundedOn, request.Method, request.Note, today), cancellationToken);
    }
}

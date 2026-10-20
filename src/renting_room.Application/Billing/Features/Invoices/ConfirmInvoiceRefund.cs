using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record ConfirmInvoiceRefundCommand(Guid Id, DateOnly RefundedOn, PaymentMethod Method, string? Note) : IRequest<Result<InvoiceDetailDto>>;

public sealed class ConfirmInvoiceRefundCommandValidator : AbstractValidator<ConfirmInvoiceRefundCommand>
{
    public ConfirmInvoiceRefundCommandValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Note).OptionalText(300);
    }
}

public sealed class ConfirmInvoiceRefundHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IContractQueries contracts, TimeProvider clock)
    : IRequestHandler<ConfirmInvoiceRefundCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(ConfirmInvoiceRefundCommand request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        return await RefundMutation.RunAsync(unitOfWork, invoices, invoiceQueries, contracts, request.Id, today,
            invoice => invoice.ConfirmRefund(request.RefundedOn, request.Method, request.Note, today), cancellationToken);
    }
}

using Mediator;
using renting_room.Application.Billing;

namespace renting_room.Application.Payments;

public sealed record ListPaymentsQuery(Guid? ContractId, Guid? RoomId, Guid? InvoiceId) : IRequest<IReadOnlyList<PaymentDto>>;

public sealed class ListPaymentsHandler(IPaymentQueries paymentQueries, IInvoiceQueries invoiceQueries)
    : IRequestHandler<ListPaymentsQuery, IReadOnlyList<PaymentDto>>
{
    private const int MaxRows = 500;

    public async ValueTask<IReadOnlyList<PaymentDto>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var payments = await paymentQueries.ListAsync(request, MaxRows, cancellationToken);
        return await PaymentMapping.ToDtosAsync(invoiceQueries, payments, cancellationToken);
    }
}

using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record InvoiceAdjustmentDto(
    Guid Id, Guid MeterReadingId, Guid FeeTypeId, DateOnly UsageStart, decimal OldValue, decimal NewValue, decimal OldQuantity,
    decimal NewQuantity, decimal OldAmount, decimal NewAmount, decimal Difference, AdjustmentHandling Handling, Guid? RoomChargeId,
    IReadOnlyList<Guid> PaymentIds, string Reason, DateTimeOffset CreatedAt, Guid? CreatedBy);

public sealed record GetInvoiceAdjustmentsQuery(Guid InvoiceId) : IRequest<Result<IReadOnlyList<InvoiceAdjustmentDto>>>;

/// <summary>M07 BL-BR-40: lịch sử điều chỉnh chỉ số của một hóa đơn, mới nhất trước.</summary>
public sealed class GetInvoiceAdjustmentsHandler(IInvoiceQueries invoices)
    : IRequestHandler<GetInvoiceAdjustmentsQuery, Result<IReadOnlyList<InvoiceAdjustmentDto>>>
{
    public async ValueTask<Result<IReadOnlyList<InvoiceAdjustmentDto>>> Handle(GetInvoiceAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        if (await invoices.GetContractIdAsync(request.InvoiceId, cancellationToken) is null)
            return BillingErrors.NotFound;
        return (await invoices.ListAdjustmentsAsync(request.InvoiceId, cancellationToken))
            .Select(a => new InvoiceAdjustmentDto(a.Id, a.MeterReadingId, a.FeeTypeId, a.UsageStart, a.OldValue, a.NewValue, a.OldQuantity,
                a.NewQuantity, a.OldAmount, a.NewAmount, a.Difference, a.Handling, a.RoomChargeId, a.PaymentIds, a.Reason, a.CreatedAt, a.CreatedBy))
            .ToList();
    }
}

public sealed record MarkInvoiceResentCommand(Guid InvoiceId) : IRequest<Result<InvoiceDetailDto>>;

/// <summary>"Đã gửi lại" — chủ trọ đã gửi hóa đơn vừa điều chỉnh cho người thuê (trước khi có M11).</summary>
public sealed class MarkInvoiceResentHandler(IUnitOfWork unitOfWork, IInvoiceRepository invoices, TimeProvider clock)
    : IRequestHandler<MarkInvoiceResentCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(MarkInvoiceResentCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(unitOfWork, invoices, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(),
            invoice => invoice.MarkResent(), cancellationToken);
}

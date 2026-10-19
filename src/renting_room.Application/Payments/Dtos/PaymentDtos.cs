using renting_room.Application.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

public sealed record PaymentAllocationDto(Guid InvoiceId, string? InvoiceNo, decimal Amount);

public sealed record PaymentDto(
    Guid Id,
    string ReceiptNo,
    Guid ContractId,
    decimal Amount,
    PaymentMethod Method,
    DateOnly PaidAt,
    string? PayerName,
    string? Reference,
    string? Note,
    PaymentStatus Status,
    PaymentKind Kind,
    DateTimeOffset? ReversedAt,
    string? ReverseReason,
    IReadOnlyList<PaymentAllocationDto> Allocations);

internal static class PaymentMapping
{
    public static async Task<IReadOnlyList<PaymentDto>> ToDtosAsync(IInvoiceQueries invoices, IReadOnlyList<Payment> payments, CancellationToken ct)
    {
        var invoiceIds = payments.SelectMany(p => p.Allocations).Select(a => a.InvoiceId).Distinct().ToList();
        var numbers = await invoices.GetNumbersAsync(invoiceIds, ct);
        return payments.Select(p => new PaymentDto(p.Id, p.ReceiptNo, p.ContractId, p.Amount, p.Method, p.PaidAt, p.PayerName, p.Reference,
                p.Note, p.Status, p.Kind, p.ReversedAt, p.ReverseReason,
                p.Allocations.Select(a => new PaymentAllocationDto(a.InvoiceId, numbers.GetValueOrDefault(a.InvoiceId), a.Amount)).ToList()))
            .ToList();
    }
}

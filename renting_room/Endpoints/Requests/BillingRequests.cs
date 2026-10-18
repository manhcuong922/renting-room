using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Endpoints;

public sealed record GenerateInvoicesRequest(
    Guid PropertyId, string BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool? RecalculateExistingDrafts, Guid? ContractId = null);

public sealed record GenerateInvoicesBulkRequest(IReadOnlyList<Guid>? PropertyIds, string BillingMonth, bool? RecalculateExistingDrafts);

public sealed record RecalculateInvoicesRequest(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool? KeepManualEdits,
    bool? StaleOnly);

public sealed record EditInvoiceLineRequest(decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? Note);

/// <param name="Settled">Phụ thu / giảm trừ đã thanh toán / đã hoàn ngay ⇒ hiện trên phiếu, không tính vào tổng (BL-BR-34).</param>
public sealed record AddInvoiceLineRequest(
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId, Guid? SourceInvoiceId,
    bool? Settled = null, DateOnly? SettledOn = null, PaymentMethod? Method = null);

public sealed record BulkAddInvoiceLineRequest(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    bool? Settled = null, DateOnly? SettledOn = null, PaymentMethod? Method = null);

public sealed record ConfirmRefundRequest(DateOnly RefundedOn, PaymentMethod Method, string? Note);

public sealed record InvoiceNoteRequest(string? Note);

public sealed record FinalizeBatchRequest(IReadOnlyList<Guid> InvoiceIds);

public sealed record VoidInvoiceRequest(string Reason);

public sealed record SaveMeterReadingsRequest(IReadOnlyList<PeriodicReadingInput> Readings);

public sealed record RecordPaymentRequest(
    decimal Amount, PaymentMethod Method, DateOnly PaidAt, Guid? InvoiceId, string? PayerName, string? Reference, string? Note);

public sealed record ReversePaymentRequest(string Reason);

public sealed record WriteOffInvoiceRequest(decimal? Amount, string Reason);

public sealed record PayAndWriteOffRequest(decimal Amount, PaymentMethod Method, DateOnly PaidAt, string? PayerName, string? Reference, string Reason);

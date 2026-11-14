using renting_room.Application.Meters;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Requests;

public sealed record GenerateInvoicesRequest(
    Guid PropertyId, string BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool? RecalculateExistingDrafts, Guid? ContractId = null,
    bool? SkipUnbilledPeriods = null);

public sealed record GenerateInvoicesBulkRequest(
    IReadOnlyList<Guid>? PropertyIds, string BillingMonth, bool? RecalculateExistingDrafts, bool? SkipUnbilledPeriods = null);

public sealed record RecalculateInvoicesRequest(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool? KeepManualEdits,
    bool? StaleOnly);

/// <summary><c>Version</c>: version phiếu đang thấy (M07 §10) — lệch ⇒ 409 <c>CONCURRENCY_CONFLICT</c>; cũng vậy ở thêm dòng, ghi chú, chốt.</summary>
public sealed record EditInvoiceLineRequest(decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? Note, uint Version);

public sealed record AddInvoiceLineRequest(
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId, Guid? SourceInvoiceId,
    uint Version, bool? Settled = null, DateOnly? SettledOn = null, PaymentMethod? Method = null);

public sealed record BulkAddInvoiceLineRequest(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    bool? Settled = null, DateOnly? SettledOn = null, PaymentMethod? Method = null);

public sealed record ConfirmRefundRequest(DateOnly RefundedOn, PaymentMethod Method, string? Note);

public sealed record InvoiceNoteRequest(string? Note, uint Version);

public sealed record FinalizeInvoiceRequest(uint Version);

public sealed record FinalizeBatchRequest(IReadOnlyList<Guid> InvoiceIds);

public sealed record VoidInvoiceRequest(string Reason);

public sealed record SaveMeterReadingsRequest(IReadOnlyList<PeriodicReadingInput> Readings);

public sealed record RecordPaymentRequest(
    decimal Amount, PaymentMethod Method, DateOnly PaidAt, Guid? InvoiceId, string? PayerName, string? Reference, string? Note);

public sealed record ReversePaymentRequest(string Reason);

/// <summary>M08 PM-BR-38: số đã thu mới + số người dùng đang thấy (chống 2 người cùng sửa). Giảm ⇒ reason bắt buộc.</summary>
public sealed record EditCollectedAmountRequest(decimal Amount, decimal ExpectedAmount, DateOnly? PaidAt, PaymentMethod? Method, string? Reason);

public sealed record WriteOffInvoiceRequest(decimal? Amount, string Reason);

public sealed record PayAndWriteOffRequest(decimal Amount, PaymentMethod Method, DateOnly PaidAt, string? PayerName, string? Reference, string Reason);

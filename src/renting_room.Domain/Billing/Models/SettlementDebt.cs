namespace renting_room.Domain.Billing;

/// <summary>BL-BR-41: hóa đơn đã chốt của HĐ còn nợ — thành dòng "Nợ chuyển từ hóa đơn …" trên hóa đơn trả phòng.</summary>
public sealed record SettlementDebt(Guid InvoiceId, string? InvoiceNo, DateOnly BillingMonth, DateOnly PeriodStart, DateOnly PeriodEnd, decimal Amount);

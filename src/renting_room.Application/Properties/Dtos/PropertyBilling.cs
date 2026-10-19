using renting_room.Domain.Billing;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

/// <summary>Cài đặt kỳ thu của khu (PR-BR-09) dùng khi tính kỳ / lập phiếu — mọi HĐ của khu dùng chung.</summary>
public sealed record PropertyBilling(BillingSchedule Schedule, ProrationMode ProrationMode, int PaymentDueDays, bool RoundInvoiceTotal = true);

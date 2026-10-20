using renting_room.Domain.Billing;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

public sealed record PropertyBilling(BillingSchedule Schedule, ProrationMode ProrationMode, int PaymentDueDays, bool RoundInvoiceTotal = true);

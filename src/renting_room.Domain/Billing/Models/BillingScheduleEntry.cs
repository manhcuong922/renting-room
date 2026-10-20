using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

public sealed record BillingScheduleEntry(DateOnly EffectiveFrom, int AnchorDay, ChargeMode ChargeMode, int AdjustDays = 0);

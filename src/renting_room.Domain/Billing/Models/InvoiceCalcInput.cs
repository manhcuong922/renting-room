using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

public sealed record InvoiceCalcInput(
    Contract Contract,
    BillingPeriod Period,
    BillingSchedule Schedule,
    ProrationMode ProrationMode,
    IReadOnlyDictionary<Guid, FeeType> FeeTypes,
    IReadOnlyList<Meter> RoomMeters,
    IReadOnlyDictionary<Guid, Guid> LastSegmentEndReadings,
    FinalSettlement? Final = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<decimal>>? RecentUsage = null,
    bool RoundTotal = false,
    IReadOnlySet<Guid>? ClosedReadingIds = null,
    IReadOnlyDictionary<Guid, string>? RoomCodes = null);

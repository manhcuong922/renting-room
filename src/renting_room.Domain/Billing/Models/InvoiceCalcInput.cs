using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <param name="ServicesBilledByNextContract">
/// M-02: HĐ đã ký lại (có HĐ nối tiếp) — HĐ mới thu trọn dịch vụ kỳ đầu (BL-BR-04) nên kỳ cuối "theo ngày" của HĐ cũ không thu dịch vụ nữa.
/// </param>
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
    IReadOnlyDictionary<Guid, string>? RoomCodes = null,
    DateOnly? GapClosingPeriodStart = null,
    bool ServicesBilledByNextContract = false);

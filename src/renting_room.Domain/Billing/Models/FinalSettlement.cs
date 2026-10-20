using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

public sealed record FinalSettlement(decimal? RentBilled, bool LastPeriodHasRegular);

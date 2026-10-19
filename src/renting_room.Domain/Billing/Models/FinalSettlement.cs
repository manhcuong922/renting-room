using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <summary>
/// Bối cảnh phiếu quyết toán: <paramref name="RentBilled"/> = tiền phòng kỳ cuối đã nằm trên phiếu thường (null = chưa thu) ⇒ không thu thêm;
/// thu nhiều hơn số ngày ở thực tế ⇒ cảnh báo <c>RENT_OVERPAID</c>, chủ trọ tự thêm dòng Hoàn trả (BL-BR-27).
/// <paramref name="LastPeriodHasRegular"/> = kỳ cuối đã có phiếu thường ⇒ dịch vụ đã thu.
/// </summary>
public sealed record FinalSettlement(decimal? RentBilled, bool LastPeriodHasRegular);

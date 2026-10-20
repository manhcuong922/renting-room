using renting_room.Domain.Contracts;

using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <param name="Charge">Tiền phòng + dịch vụ kỳ cuối (M05 CT-BR-11).</param>
/// <param name="RentInvoiceId">Hóa đơn tháng đã lập tiền phòng kỳ cuối (khu thu trước) — nguồn của dòng Hoàn trả gợi ý.</param>
/// <param name="RentCollected">Người thuê đã trả thật cho hóa đơn đó (đã thu − bỏ nợ) — gợi ý hoàn không vượt (BL-BR-27).</param>
/// <param name="RentRefunded">Các dòng hoàn trả đã trỏ tới hóa đơn đó.</param>
public sealed record FinalSettlement(
    decimal? RentBilled, bool LastPeriodHasRegular, FinalPeriodCharge Charge = FinalPeriodCharge.FullPeriod, Guid? RentInvoiceId = null,
    decimal RentCollected = 0, decimal RentRefunded = 0);

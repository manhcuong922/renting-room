using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <param name="FeeTypes">Dịch vụ của HĐ + điện nước có công tơ ở phòng, kèm bảng giá.</param>
/// <param name="LastSegmentEndReadings">Theo công tơ: chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy (kỳ trước) của HĐ.</param>
/// <param name="Schedule">Lịch kỳ thu của khu (ngày chốt, thu trước / thu sau theo từng kỳ — PR-BR-09).</param>
/// <param name="ProrationMode">Cách tính tiền phòng kỳ lẻ của khu.</param>
/// <param name="Final">Có ⇒ tính phiếu quyết toán (BL-BR-17); null ⇒ phiếu thường.</param>
/// <param name="RecentUsage">Theo khoản điện nước: sản lượng các phiếu thường trước của HĐ, mới nhất trước (MT-BR-08).</param>
/// <param name="RoundTotal">Khu bật làm tròn tổng phiếu xuống nghìn (BL-BR-29).</param>
/// <param name="ClosedReadingIds">Chỉ số cuối của các khoản chốt điện nước khi chuyển phòng còn hiệu lực (dữ liệu trước 17/10/2026 — BL-BR-38):
/// đoạn đo kết thúc ở đó đã thu bằng khoản phát sinh, phiếu không tính lại.</param>
/// <param name="RoomCodes">Mã phòng theo id — chụp vào đoạn đo để hiển thị (phòng cũ / phòng mới khi chuyển phòng).</param>
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

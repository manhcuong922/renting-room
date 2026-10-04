namespace renting_room.Application.Common.Interfaces;

/// <summary>
/// Ngày cuối của dòng phiếu đã chốt có dùng khoản thu (FE-BR-07). Hiện thực thật ở M07;
/// trước khi có M07 trả null = chưa kỳ nào bị khóa.
/// </summary>
public interface IFeePriceLockReader
{
    Task<DateOnly?> GetLockedUntilAsync(Guid feeTypeId, CancellationToken cancellationToken);
}

/// <summary>Cấu hình khoản thu (FE-BR-13). Đọc từ <c>Fees:ElectricityPriceWarningThreshold</c>.</summary>
public interface IFeeSettings
{
    /// <summary>Đơn giá điện (đ/kWh) cao hơn mức này ⇒ cảnh báo (TT 60/2025: tiền điện thu không vượt giá bán lẻ).</summary>
    decimal ElectricityPriceWarningThreshold { get; }
}

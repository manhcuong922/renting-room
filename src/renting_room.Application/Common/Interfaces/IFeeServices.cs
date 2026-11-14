namespace renting_room.Application.Common.Interfaces;

public interface IFeePriceLockReader
{
    /// <summary>Ngày cuối (không trùng) của các dòng phiếu đã chốt dùng khoản thu — nơi giá áp được tra (FE-BR-07).</summary>
    Task<IReadOnlyCollection<DateOnly>> GetLockedDatesAsync(Guid feeTypeId, CancellationToken cancellationToken);
}

/// <summary>Cấu hình khoản thu (FE-BR-13). Đọc từ <c>Fees:ElectricityPriceWarningThreshold</c>.</summary>
public interface IFeeSettings
{
    /// <summary>Đơn giá điện (đ/kWh) cao hơn mức này ⇒ cảnh báo (TT 60/2025: tiền điện thu không vượt giá bán lẻ).</summary>
    decimal ElectricityPriceWarningThreshold { get; }
}

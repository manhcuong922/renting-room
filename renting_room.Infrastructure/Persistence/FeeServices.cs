using Microsoft.Extensions.Options;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Persistence;

public sealed class FeeOptions
{
    public const string SectionName = "Fees";

    /// <summary>
    /// Mặc định 3.460đ/kWh = giá bán lẻ điện sinh hoạt bậc 6 (chưa VAT) — cập nhật khi EVN đổi biểu giá.
    /// Chỉ dùng để cảnh báo, không chặn.
    /// </summary>
    public decimal ElectricityPriceWarningThreshold { get; init; } = 3460;
}

internal sealed class FeeSettings(IOptions<FeeOptions> options) : IFeeSettings
{
    public decimal ElectricityPriceWarningThreshold => options.Value.ElectricityPriceWarningThreshold;
}

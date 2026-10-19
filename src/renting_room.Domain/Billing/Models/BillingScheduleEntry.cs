using renting_room.Domain.Properties;

namespace renting_room.Domain.Billing;

/// <summary>Một mốc của lịch kỳ thu: từ <paramref name="EffectiveFrom"/> dùng ngày chốt / cách thu này (mốc đầu = <see cref="DateOnly.MinValue"/>).</summary>
/// <param name="AdjustDays">Số ngày tiền phòng cộng (+) / trừ (−) ở kỳ chuyển tiếp bắt đầu tại <paramref name="EffectiveFrom"/> (BL-BR-28).</param>
public sealed record BillingScheduleEntry(DateOnly EffectiveFrom, int AnchorDay, ChargeMode ChargeMode, int AdjustDays = 0);

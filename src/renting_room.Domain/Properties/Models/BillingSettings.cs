namespace renting_room.Domain.Properties;

public sealed record BillingSettings(
    int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays, bool RoundInvoiceTotal = true)
{
    /// <summary>Mặc định khu mới (chốt 09/10/2026): chốt ngày 1, <b>thu sau</b>, tính theo ngày ở kỳ lẻ, hạn 5 ngày, báo trước 30 ngày, làm tròn nghìn.</summary>
    public static readonly BillingSettings Standard = new(1, ChargeMode.Postpaid, 5, ProrationMode.Daily, 30);

    public void EnsureValid()
    {
        if (AnchorDay is < Billing.BillingSchedule.MinAnchorDay or > Billing.BillingSchedule.MaxAnchorDay)
            throw new ArgumentOutOfRangeException(nameof(AnchorDay));
        if (PaymentDueDays is < 0 or > 60)
            throw new ArgumentOutOfRangeException(nameof(PaymentDueDays));
        if (NoticeDays is < 0 or > 180)
            throw new ArgumentOutOfRangeException(nameof(NoticeDays));
    }
}

using renting_room.Application.Billing;
using renting_room.Domain.Billing;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

/// <summary>
/// M05 CT-UC-08: còn phần chưa thu của kỳ cuối ⇒ cần hóa đơn trả phòng. Không cần khi ngày trả phòng là ngày cuối kỳ, kỳ đó đã có hóa đơn
/// tháng và khu thu sau — tiền phòng, dịch vụ, điện của kỳ đều đã tính (số chốt kỳ là số cuối của HĐ).
/// </summary>
public static class FinalInvoiceNeed
{
    public static bool IsRequired(BillingSchedule schedule, DateOnly billingStart, DateOnly actualEnd, IEnumerable<InvoicePeriodRow> invoices)
    {
        var periods = schedule.ContractPeriods(billingStart, actualEnd, actualEnd);
        if (periods.Count == 0)
            return true;
        var last = periods[^1];
        return !(schedule.StandardPeriodContaining(last.Start).End == actualEnd
            && schedule.ChargeModeOn(last.Start) == ChargeMode.Postpaid
            && invoices.Any(i => i.Type == InvoiceType.Regular && i.PeriodStart == last.Start));
    }

    /// <summary>Ngày cuối của kỳ đã chốt hóa đơn tháng gần nhất — "Coi như trả phòng từ ngày này" khi các kỳ sau là ảo.</summary>
    public static DateOnly? LastBilledEnd(IEnumerable<InvoicePeriodRow> invoices) =>
        invoices.Where(i => i is { Type: InvoiceType.Regular, Status: InvoiceStatus.Finalized }).Select(i => (DateOnly?)i.PeriodEnd).Max();
}

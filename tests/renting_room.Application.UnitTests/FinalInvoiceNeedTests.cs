using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Properties;

namespace renting_room.Application.UnitTests;

/// <summary>M05 CT-UC-08: khi nào thanh lý cần hóa đơn trả phòng.</summary>
public sealed class FinalInvoiceNeedTests
{
    private static readonly BillingSchedule Anchor1 = BillingSchedule.Single(1, ChargeMode.Postpaid);

    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static InvoicePeriodRow Regular(string start, string end) => new(InvoiceType.Regular, InvoiceStatus.Finalized, D(start), D(end));

    [Fact]
    public void IsRequired_IsFalse_WhenTenantLeftBeforeBillingStarted()
    {
        // HĐ bắt đầu 01/10, tính tiền từ 15/10, người thuê đi 10/10 ⇒ không có kỳ nào để tính.
        var required = FinalInvoiceNeed.IsRequired(Anchor1, D("2026-10-15"), D("2026-10-10"), []);

        required.Should().BeFalse();
    }

    [Fact]
    public void IsRequired_IsTrue_WhenLastPeriodHasNothingBilledYet() =>
        FinalInvoiceNeed.IsRequired(Anchor1, D("2026-10-01"), D("2026-10-20"), []).Should().BeTrue();

    [Fact]
    public void IsRequired_IsFalse_WhenEndIsPeriodEndAndPostpaidPeriodAlreadyBilled() =>
        FinalInvoiceNeed.IsRequired(Anchor1, D("2026-10-01"), D("2026-10-31"), [Regular("2026-10-01", "2026-10-31")]).Should().BeFalse();

    [Fact]
    public void IsRequired_IsTrue_WhenEndIsMidPeriodEvenIfPeriodWasBilled() =>
        FinalInvoiceNeed.IsRequired(Anchor1, D("2026-10-01"), D("2026-10-20"), [Regular("2026-10-01", "2026-10-31")]).Should().BeTrue();
}

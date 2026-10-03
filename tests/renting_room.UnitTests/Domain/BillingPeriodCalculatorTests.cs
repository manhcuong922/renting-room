using renting_room.Domain.Billing;

namespace renting_room.UnitTests.Domain;

public sealed class BillingPeriodCalculatorTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Theory]
    [InlineData(2026, 2, 31, "2026-02-28")] // tháng 2 không nhuận: kẹp về 28
    [InlineData(2028, 2, 31, "2028-02-29")] // năm nhuận
    [InlineData(2026, 4, 31, "2026-04-30")]
    [InlineData(2026, 10, 5, "2026-10-05")]
    public void AnchorDate_ClampsToEndOfMonth(int year, int month, int anchor, string expected)
    {
        BillingPeriodCalculator.AnchorDate(year, month, anchor).Should().Be(D(expected));
    }

    [Fact]
    public void Periods_StartOnAnchorDay_AreFullMonths()
    {
        var periods = BillingPeriodCalculator.Periods(D("2026-10-05"), null, 5, D("2026-12-31"));

        periods.Should().Equal(
            new BillingPeriod(D("2026-10-05"), D("2026-11-04")),
            new BillingPeriod(D("2026-11-05"), D("2026-12-04")),
            new BillingPeriod(D("2026-12-05"), D("2027-01-04")));
    }

    [Fact]
    public void FirstPeriod_StartingAfterAnchor_EndsBeforeNextAnchor()
    {
        var first = BillingPeriodCalculator.Periods(D("2026-10-20"), null, 5, D("2026-10-31"))[0];

        first.Should().Be(new BillingPeriod(D("2026-10-20"), D("2026-11-04")));
    }

    [Fact]
    public void FirstPeriod_StartingBeforeAnchorInSameMonth_IsMergedIntoNextPeriod()
    {
        // C-05: bắt đầu 03/10, chốt ngày 5 ⇒ không tạo kỳ 03/10–04/10 riêng (2 kỳ cùng tháng 10).
        var periods = BillingPeriodCalculator.Periods(D("2026-10-03"), null, 5, D("2026-11-30"));

        periods[0].Should().Be(new BillingPeriod(D("2026-10-03"), D("2026-11-04")));
        periods[1].Should().Be(new BillingPeriod(D("2026-11-05"), D("2026-12-04")));
        periods.Select(p => p.BillingMonth).Should().OnlyHaveUniqueItems().And.StartWith("2026-10");
    }

    [Fact]
    public void Anchor31_ProducesContiguousPeriodsThroughShortMonths()
    {
        var periods = BillingPeriodCalculator.Periods(D("2026-01-31"), null, 31, D("2026-05-31"));

        periods.Should().Equal(
            new BillingPeriod(D("2026-01-31"), D("2026-02-27")),
            new BillingPeriod(D("2026-02-28"), D("2026-03-30")),
            new BillingPeriod(D("2026-03-31"), D("2026-04-29")),
            new BillingPeriod(D("2026-04-30"), D("2026-05-30")),
            new BillingPeriod(D("2026-05-31"), D("2026-06-29")));

        // Liên tục, không chồng/hở ngày.
        periods.Zip(periods.Skip(1)).Should().OnlyContain(pair => pair.First.End.AddDays(1) == pair.Second.Start);
    }

    [Fact]
    public void LastPeriod_IsTruncatedAtActualEnd()
    {
        var periods = BillingPeriodCalculator.Periods(D("2026-10-05"), D("2026-11-20"), 5, D("2027-12-31"));

        periods.Should().HaveCount(2);
        periods[^1].Should().Be(new BillingPeriod(D("2026-11-05"), D("2026-11-20")));
    }

    [Fact]
    public void IsPeriodStart_And_PeriodContaining()
    {
        BillingPeriodCalculator.IsPeriodStart(D("2026-10-20"), 5, D("2026-11-05")).Should().BeTrue();
        BillingPeriodCalculator.IsPeriodStart(D("2026-10-20"), 5, D("2026-11-10")).Should().BeFalse();

        BillingPeriodCalculator.PeriodContaining(D("2026-10-05"), null, 5, D("2026-11-30"))
            .Should().Be(new BillingPeriod(D("2026-11-05"), D("2026-12-04")));
        BillingPeriodCalculator.PeriodContaining(D("2026-10-05"), null, 5, D("2026-10-01")).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void InvalidAnchorDay_Throws(int anchor)
    {
        var act = () => BillingPeriodCalculator.AnchorDate(2026, 1, anchor);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

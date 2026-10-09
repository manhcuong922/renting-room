using renting_room.Domain.Billing;
using renting_room.Domain.Properties;

namespace renting_room.UnitTests.Domain;

/// <summary>C-05 / PR-BR-09 / K4 / K5 — kỳ thu theo lịch kỳ thu của khu.</summary>
public sealed class BillingScheduleTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static BillingPeriod P(string start, string end, string month) => new(D(start), D(end), D(month + "-01"));

    private static readonly BillingSchedule Anchor5 = BillingSchedule.Single(5, ChargeMode.Postpaid);

    [Theory]
    [InlineData(2026, 2, 28, "2026-02-28")]
    [InlineData(2028, 2, 28, "2028-02-28")]
    [InlineData(2026, 10, 5, "2026-10-05")]
    public void AnchorDate_IsTheAnchorDayOfMonth(int year, int month, int anchor, string expected) =>
        BillingSchedule.AnchorDate(year, month, anchor).Should().Be(D(expected));

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public void AnchorDay_OutsideOneTo28_IsRejected(int anchor)
    {
        var act = () => BillingSchedule.Single(anchor, ChargeMode.Postpaid);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void StandardPeriods_RunFromAnchorToAnchor_MonthIsStartMonth()
    {
        var periods = Anchor5.ContractPeriods(D("2026-10-05"), null, D("2026-12-31"));

        periods.Should().Equal(
            P("2026-10-05", "2026-11-04", "2026-10"),
            P("2026-11-05", "2026-12-04", "2026-11"),
            P("2026-12-05", "2027-01-04", "2026-12"));
    }

    [Theory]
    [InlineData("2026-11-03", "2026-11-04")] // vào 03/11 ⇒ kỳ đầu 2 ngày, không gộp
    [InlineData("2026-10-20", "2026-11-04")]
    public void FirstPeriod_IsNotMerged_AndBelongsToTheKhuMonth(string start, string firstEnd)
    {
        var first = Anchor5.ContractPeriods(D(start), null, D(start))[0];

        first.Should().Be(new BillingPeriod(D(start), D(firstEnd), D("2026-10-01")));
    }

    [Fact]
    public void BillingStartDate_LaterThanContractStart_SkipsEarlierDays()
    {
        // K5: HĐ nhập từ sổ cũ bắt đầu 03/2026, tính tiền trong phần mềm từ 05/10.
        var periods = Anchor5.ContractPeriods(D("2026-10-05"), null, D("2026-11-30"));

        periods[0].Start.Should().Be(D("2026-10-05"));
        periods.Should().HaveCount(2);
    }

    [Fact]
    public void Anchor28_FebruaryHasAPeriodEveryMonth()
    {
        var periods = BillingSchedule.Single(28, ChargeMode.Postpaid).ContractPeriods(D("2026-01-28"), null, D("2026-03-28"));

        periods.Should().Equal(
            P("2026-01-28", "2026-02-27", "2026-01"),
            P("2026-02-28", "2026-03-27", "2026-02"),
            P("2026-03-28", "2026-04-27", "2026-03"));
    }

    [Fact]
    public void ActualEnd_CutsTheLastPeriod()
    {
        var periods = Anchor5.ContractPeriods(D("2026-10-05"), D("2026-11-20"), D("2027-12-31"));

        periods[^1].Should().Be(P("2026-11-05", "2026-11-20", "2026-11"));
    }

    [Fact]
    public void RentFactor_Daily_IsDaysOverStandardLength_FullPeriodIsOneMonth()
    {
        Anchor5.RentFactor(D("2026-11-03"), D("2026-11-04"), ProrationMode.Daily).Should().Be(2m / 31);
        Anchor5.RentFactor(D("2026-11-03"), D("2026-11-04"), ProrationMode.FullPeriod).Should().Be(1m);
        Anchor5.RentFactor(D("2026-11-05"), D("2026-12-04"), ProrationMode.Daily).Should().Be(1m);
    }

    // ------------------------------------------------------------------ K4: đổi ngày chốt khi khu đã có phiếu

    [Fact]
    public void ChangeAnchorLater_TransitionIsLonger_StaysInTheSameMonth()
    {
        // Khu chốt ngày 1 đã lập phiếu tháng 10, đổi sang ngày 5 từ 01/11 ⇒ kỳ chuyển tiếp 01/11–04/12 (dư 4 ngày) vẫn là tháng 11.
        var schedule = BillingSchedule.Single(1, ChargeMode.Postpaid).ChangeFrom(D("2026-11-01"), 5, ChargeMode.Postpaid, adjustDays: null);

        var periods = schedule.ContractPeriods(D("2026-10-01"), null, D("2026-12-31"));

        periods.Should().Equal(
            P("2026-10-01", "2026-10-31", "2026-10"),
            P("2026-11-01", "2026-12-04", "2026-11"),
            P("2026-12-05", "2027-01-04", "2026-12"));
        var transition = schedule.StandardPeriodContaining(D("2026-11-15"));
        transition.DeviationDays.Should().Be(4);
        transition.AdjustDays.Should().Be(4, "lệch từ 4 ngày ⇒ gợi ý tính đủ số ngày dư");
        schedule.RentFactor(D("2026-11-01"), D("2026-12-04"), ProrationMode.Daily).Should().Be(1m + 4m / 30);
    }

    [Fact]
    public void ChangeAnchorEarlier_TransitionIsShorter_SuggestsDeduction()
    {
        var schedule = BillingSchedule.Single(5, ChargeMode.Postpaid).ChangeFrom(D("2026-11-05"), 1, ChargeMode.Postpaid, adjustDays: null);

        var transition = schedule.StandardPeriodContaining(D("2026-11-05"));

        transition.Should().Match<StandardPeriod>(t => t.Start == D("2026-11-05") && t.End == D("2026-11-30") && t.Month == D("2026-11-01"));
        transition.DeviationDays.Should().Be(-4);
        transition.AdjustDays.Should().Be(-4);
        schedule.StandardPeriodContaining(D("2026-12-01")).Start.Should().Be(D("2026-12-01"));
    }

    [Theory]
    [InlineData(3, 0)]  // lệch 2 ngày ⇒ không đáng thu
    [InlineData(4, 0)]  // lệch 3 ngày ⇒ vẫn 0
    [InlineData(5, 4)]  // lệch 4 ngày ⇒ đủ 4 ngày
    public void SuggestedAdjust_IgnoresSmallDeviation(int newAnchor, int expectedAdjust)
    {
        var schedule = BillingSchedule.Single(1, ChargeMode.Postpaid).ChangeFrom(D("2026-11-01"), newAnchor, ChargeMode.Postpaid, null);

        schedule.StandardPeriodContaining(D("2026-11-01")).AdjustDays.Should().Be(expectedAdjust);
    }

    [Theory]
    [InlineData(4, 0, true)]
    [InlineData(4, 4, true)]
    [InlineData(4, 5, false)]
    [InlineData(4, -1, false)]
    [InlineData(-4, -4, true)]
    [InlineData(-4, 1, false)]
    public void AdjustDays_StayBetweenZeroAndDeviation(int deviation, int adjust, bool allowed) =>
        BillingSchedule.IsAdjustAllowed(deviation, adjust).Should().Be(allowed);

    [Fact]
    public void ChangingAgainBeforeTransitionIsBilled_Overwrites_RevertingRemovesIt()
    {
        var original = BillingSchedule.Single(1, ChargeMode.Postpaid);
        var changed = original.ChangeFrom(D("2026-11-01"), 5, ChargeMode.Postpaid, 0);

        changed.ChangeFrom(D("2026-11-01"), 10, ChargeMode.Postpaid, 0).Entries.Should().HaveCount(2)
            .And.Contain(e => e.AnchorDay == 10);
        changed.ChangeFrom(D("2026-11-01"), 1, ChargeMode.Postpaid, null).Entries.Should().ContainSingle();
    }

    [Fact]
    public void ChargeModeSwitch_AppliesFromTheChangePeriod()
    {
        var schedule = BillingSchedule.Single(1, ChargeMode.Postpaid).ChangeFrom(D("2026-11-01"), 1, ChargeMode.Prepaid, null);

        schedule.ChargeModeOn(D("2026-10-31")).Should().Be(ChargeMode.Postpaid);
        schedule.ChargeModeOn(D("2026-11-01")).Should().Be(ChargeMode.Prepaid);
        schedule.StandardPeriodContaining(D("2026-11-10")).Should()
            .Match<StandardPeriod>(p => p.Start == D("2026-11-01") && p.End == D("2026-11-30") && p.AdjustDays == 0 && !p.IsTransition);
    }
}

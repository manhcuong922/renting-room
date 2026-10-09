using renting_room.Domain.Meters;

namespace renting_room.UnitTests.Domain;

/// <summary>MT-BR-08 — ngưỡng cảnh báo điện nước bất thường.</summary>
public sealed class UsageAnomalyTests
{
    [Theory]
    [InlineData(450, true)]   // trung bình 140 ⇒ gấp 3,2 lần và tăng 310
    [InlineData(400, false)]  // chưa tới 3 lần
    public void Spike_NeedsThreeTimesAverage(decimal quantity, bool warned) =>
        (UsageAnomaly.Check("Điện", "kWh", quantity, [150, 130, 140], hasOccupants: true) is not null).Should().Be(warned);

    [Fact]
    public void SmallUsageRoom_IsNotWarned_UnlessIncreaseIsAtLeast50()
    {
        UsageAnomaly.Check("Điện", "kWh", 35, [10, 10, 10], true).Should().BeNull("10 → 35 kWh tăng chưa tới 50");
        UsageAnomaly.Check("Điện", "kWh", 61, [10, 10, 10], true).Should().Contain("gấp");
    }

    [Fact]
    public void LessThanThreePeriods_SpikeIsNotChecked() =>
        UsageAnomaly.Check("Điện", "kWh", 900, [100, 100], true).Should().BeNull();

    [Fact]
    public void Zero_IsWarnedOnlyWhenSomeoneLivesThere()
    {
        UsageAnomaly.Check("Nước", "m³", 0, [], hasOccupants: true).Should().Contain("= 0");
        UsageAnomaly.Check("Nước", "m³", 0, [], hasOccupants: false).Should().BeNull();
    }

    [Fact]
    public void RecentAverage_IsNullUntilThreePeriods()
    {
        UsageAnomaly.RecentAverage([1, 2]).Should().BeNull();
        UsageAnomaly.RecentAverage([150, 130, 140, 999]).Should().Be(140);
    }
}

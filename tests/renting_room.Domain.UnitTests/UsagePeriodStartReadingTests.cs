using renting_room.Domain.Billing;
using renting_room.Domain.Meters;

namespace renting_room.Domain.UnitTests;

/// <summary>M-10 (M06 §3.3 bước 2(b)): kỳ đầu của HĐ tính từ chỉ số nhận phòng của chính HĐ đó, không từ chỉ số của người trước ghi cùng ngày.</summary>
public sealed class UsagePeriodStartReadingTests
{
    private static readonly DateOnly Nov1 = new(2026, 11, 1);
    private static readonly Guid OldContract = Guid.NewGuid();
    private static readonly Guid NewContract = Guid.NewGuid();

    private static readonly UsagePeriod FirstPeriod = new(Nov1, new DateOnly(2026, 11, 30), Nov1, false);

    private static Meter MeterWithSameDayReadings()
    {
        var meter = Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 9, 1), 1000, null);
        meter.Record(ReadingKind.Final, new DateOnly(2026, 10, 31), 1200, OldContract, null).IsSuccess.Should().BeTrue();
        // Người cũ còn ghi số chốt kỳ sáng 01/11 (1.250); người mới nhận phòng cùng ngày ở 1.200 (xếp trước số chốt vì Handover < Periodic).
        meter.Record(ReadingKind.Handover, Nov1, 1200, NewContract, null).IsSuccess.Should().BeTrue();
        meter.Record(ReadingKind.Periodic, Nov1, 1250, OldContract, null, new DateOnly(2026, 10, 1)).IsSuccess.Should().BeTrue();
        return meter;
    }

    [Fact]
    public void FirstPeriod_StartsAtOwnHandover_EvenWhenPreviousTenantReadingSharesTheDay()
    {
        var meter = MeterWithSameDayReadings();

        var start = FirstPeriod.StartReading(meter, null, NewContract);

        start!.Kind.Should().Be(ReadingKind.Handover);
        start.Value.Should().Be(1200);
    }

    [Fact]
    public void FirstPeriod_WithoutHandover_FallsBackToLatestReadingOnOrBeforeStart()
    {
        var meter = MeterWithSameDayReadings();

        FirstPeriod.StartReading(meter, null, Guid.NewGuid())!.Value.Should().Be(1250);
    }
}

using renting_room.Domain.Meters;

namespace renting_room.UnitTests.Domain;

public sealed class MeterTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static readonly Guid Contract = Guid.NewGuid();

    private static Meter NewMeter(decimal initial = 100) =>
        Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "E-101", Day, initial, null);

    [Fact]
    public void Install_RecordsInitialReading_AsLatest()
    {
        var meter = NewMeter();

        meter.IsActive.Should().BeTrue();
        meter.Latest!.Kind.Should().Be(ReadingKind.Initial);
        meter.LatestOnOrBefore(Day.AddDays(10))!.Value.Should().Be(100);
        meter.LatestOnOrBefore(Day.AddDays(-1)).Should().BeNull();
    }

    [Fact]
    public void Readings_MustNotDecrease_InTimeOrder()
    {
        var meter = NewMeter();
        meter.Record(ReadingKind.Handover, Day.AddDays(5), 99, Contract, null).Error!.Code.Should().Be("READING_NOT_MONOTONIC");
        meter.Record(ReadingKind.Handover, Day.AddDays(5), 108, Contract, null).IsSuccess.Should().BeTrue("người cũ đi ở 100, sửa chữa dùng 8");
        meter.Record(ReadingKind.Final, Day.AddDays(40), 150, Contract, null).IsSuccess.Should().BeTrue();

        // Sửa chỉ số giữa: phải nằm trong [100, 150].
        var handover = meter.Readings.Single(r => r.Kind == ReadingKind.Handover);
        meter.Correct(handover.Id, 151, null).Error!.Details!["nextValue"].Should().Be(150m);
        meter.Correct(handover.Id, 120, "Đọc lại").IsSuccess.Should().BeTrue();
        handover.Value.Should().Be(120);
    }

    [Fact]
    public void Record_OutsideActivePeriod_IsRejected()
    {
        var meter = NewMeter();
        meter.Record(ReadingKind.Adhoc, Day.AddDays(-1), 100, null, null).Error.Should().Be(MeterErrors.InvalidReadingDate);
    }

    [Fact]
    public void Replace_RemovesOldWithFinalValue_InstallsNewSameDay()
    {
        var meter = NewMeter();
        var replaceDay = Day.AddDays(15);

        meter.ReplaceWith(replaceDay, 90, "E-102", 0, "Hỏng").Error!.Code.Should().Be("READING_NOT_MONOTONIC");
        var next = meter.ReplaceWith(replaceDay, 170, "E-102", 0, "Hỏng").Value!;

        meter.IsActive.Should().BeFalse();
        meter.RemovedDate.Should().Be(replaceDay);
        meter.ReplacedByMeterId.Should().Be(next.Id);
        meter.IsActiveOn(replaceDay).Should().BeFalse("ngày thay thuộc về công tơ mới");
        next.IsActiveOn(replaceDay).Should().BeTrue();
        next.Latest!.Value.Should().Be(0);
        meter.Remove(replaceDay.AddDays(1), 200, null).Error.Should().Be(MeterErrors.Removed);
        meter.Record(ReadingKind.Adhoc, replaceDay, 175, null, null).Error.Should().Be(MeterErrors.InvalidReadingDate);
    }

    [Fact]
    public void Remove_BeforeExistingReading_IsRejected()
    {
        var meter = NewMeter();
        meter.Record(ReadingKind.Adhoc, Day.AddDays(20), 130, null, null);
        meter.Remove(Day.AddDays(10), 140, null).Error.Should().Be(MeterErrors.InvalidReadingDate);
    }
}

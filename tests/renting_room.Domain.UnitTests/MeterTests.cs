using renting_room.Domain.Meters;

namespace renting_room.Domain.UnitTests;

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
    public void FindHandoverAndFinal_AreScopedToTheStay_WhenContractLeavesAndReturnsToTheRoom()
    {
        // MT-BR-04: HĐ ở phòng [1/10, 11/10], chuyển đi, quay lại từ 21/10 — mỗi lần ở có chỉ số nhận phòng / chỉ số cuối riêng.
        var meter = NewMeter();
        meter.Record(ReadingKind.Handover, Day, 100, Contract, null);
        meter.Record(ReadingKind.Final, Day.AddDays(10), 130, Contract, null);
        meter.Record(ReadingKind.Handover, Day.AddDays(20), 138, Contract, null);

        meter.FindHandover(Contract, Day, Day.AddDays(10))!.Value.Should().Be(100);
        meter.FindHandover(Contract, Day.AddDays(20))!.Value.Should().Be(138);
        meter.FindFinal(Contract, Day.AddDays(20)).Should().BeNull("chỉ số cuối 130 thuộc lần ở trước");

        meter.Record(ReadingKind.Final, Day.AddDays(40), 170, Contract, null).IsSuccess.Should().BeTrue();
        meter.FindFinal(Contract, Day.AddDays(20))!.Value.Should().Be(170);
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
    public void Correct_WithNewDate_MovesTheReading_AndChecksOrderAtTheNewPosition()
    {
        // L-10: sửa chỉ số cuối kỳ kèm ngày ghi mới — đơn điệu xét theo vị trí ngày mới.
        var meter = NewMeter();
        meter.Record(ReadingKind.Handover, Day, 100, Contract, null);
        meter.Record(ReadingKind.Adhoc, Day.AddDays(20), 140, null, null);
        var periodic = meter.Record(ReadingKind.Periodic, Day.AddDays(30), 150, Contract, null, Day).Value!;

        meter.Correct(periodic.Id, 150, null, Day.AddDays(10)).Error!.Code.Should().Be("READING_NOT_MONOTONIC", "ngày 11/10 đứng trước số kiểm tra 140");
        meter.Correct(periodic.Id, 150, null, Day.AddDays(-1)).Error.Should().Be(MeterErrors.InvalidReadingDate);
        periodic.ReadingDate.Should().Be(Day.AddDays(30));

        meter.Correct(periodic.Id, 145, null, Day.AddDays(25)).IsSuccess.Should().BeTrue();
        periodic.ReadingDate.Should().Be(Day.AddDays(25));
        periodic.Value.Should().Be(145);
        meter.Correct(periodic.Id, 145, null, Day.AddDays(28)).IsSuccess.Should().BeTrue("chỉ đổi ngày");
        periodic.ReadingDate.Should().Be(Day.AddDays(28));
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

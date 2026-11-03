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
        // Người cũ còn ghi số chốt kỳ sáng 01/11 (1.250); người mới nhận phòng cùng ngày ở 1.200 (HĐ vào sau xếp sau, không so với số của người cũ).
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

    // H-03 (M06 MT-BR-12 bước 2(g)): kỳ sau lấy số đầu từ chỉ số chốt kỳ liền trước trên lưới, không theo đoạn đo của phiếu trước.
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly UsagePeriod November = new(Nov1, new DateOnly(2026, 11, 30), Nov1, false, PreviousClosingPeriodStart: Oct1);

    private static (Meter Meter, MeterReading Sep, MeterReading Oct) MeterWithClosings()
    {
        var meter = Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 8, 31), 900, null);
        var sep = meter.Record(ReadingKind.Periodic, new DateOnly(2026, 9, 30), 1000, NewContract, null, new DateOnly(2026, 9, 1)).Value!;
        var oct = meter.Record(ReadingKind.Periodic, new DateOnly(2026, 10, 31), 1080, NewContract, null, Oct1).Value!;
        return (meter, sep, oct);
    }

    [Fact]
    public void NextPeriod_StartsAtPreviousClosingOnGrid_NotAtLastInvoiceSegment()
    {
        var (meter, sep, oct) = MeterWithClosings();

        // Phiếu gần nhất còn đoạn đo kết thúc ở chốt tháng 9 (nháp tháng 10 mất đoạn đo) ⇒ vẫn lấy chốt tháng 10.
        November.StartReading(meter, sep.Id, NewContract)!.Id.Should().Be(oct.Id);
    }

    [Fact]
    public void NextPeriod_PreviousClosingVoided_HasNoStart_InsteadOfOlderReading()
    {
        var (meter, sep, oct) = MeterWithClosings();
        meter.VoidEntry(oct.Id, "Ghi nhầm", DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();

        November.StartReading(meter, sep.Id, NewContract).Should().BeNull();
    }

    [Fact]
    public void NextPeriod_MeterInstalledDuringPeriod_StartsAtInitial()
    {
        var meter = Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 11, 10), 0, null);

        November.StartReading(meter, null, NewContract)!.Kind.Should().Be(ReadingKind.Initial);
    }

    [Fact]
    public void FirstPeriod_WithoutHandover_FallsBackToLatestReadingOnOrBeforeStart()
    {
        var meter = MeterWithSameDayReadings();

        // M-10: cùng ngày HĐ vào sau xếp sau ⇒ số mới nhất là số nhận phòng của người mới, không phải số chốt kỳ 1.250 của người cũ.
        FirstPeriod.StartReading(meter, null, Guid.NewGuid())!.Value.Should().Be(1200);
    }
}

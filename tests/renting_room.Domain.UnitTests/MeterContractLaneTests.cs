using renting_room.Domain.Meters;

namespace renting_room.Domain.UnitTests;

/// <summary>M-10 (28/10/2026, M06 MT-BR-03): mỗi HĐ có dải số riêng trên công tơ — chỉ số nhận phòng của HĐ mới không so với chỉ số của HĐ khác.</summary>
public sealed class MeterContractLaneTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    /// <summary>Lắp ở 100; A nhận phòng 100, chốt kỳ nhầm 250 (thật 150), số cuối cũng 250 (ngày 41).</summary>
    private static Meter MeterWithTenantA()
    {
        var meter = Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "E-101", Day, 100, null);
        meter.Record(ReadingKind.Handover, Day, 100, A, null).IsSuccess.Should().BeTrue();
        meter.Record(ReadingKind.Periodic, Day.AddDays(30), 250, A, null, Day).IsSuccess.Should().BeTrue();
        meter.Record(ReadingKind.Final, Day.AddDays(40), 250, A, null).IsSuccess.Should().BeTrue();
        return meter;
    }

    [Fact]
    public void NewContractHandover_BelowPreviousContractReadings_IsAccepted_ThenOwnLaneIsMonotonic()
    {
        var meter = MeterWithTenantA();

        meter.Record(ReadingKind.Handover, Day.AddDays(41), 200, B, null).IsSuccess.Should().BeTrue("người cũ gõ nhầm 250, người mới theo công tơ thật 200");
        meter.Record(ReadingKind.Periodic, Day.AddDays(60), 190, B, null, Day.AddDays(41)).Error!.Code.Should().Be("READING_NOT_MONOTONIC");
        meter.Record(ReadingKind.Periodic, Day.AddDays(60), 230, B, null, Day.AddDays(41)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Handover_BelowInitialReading_IsStillRefused()
    {
        var meter = MeterWithTenantA();

        meter.Record(ReadingKind.Handover, Day.AddDays(41), 90, B, null).Error!.Code.Should().Be("READING_NOT_MONOTONIC");
    }

    [Fact]
    public void SameDay_NewContractHandover_IsNotComparedWithPreviousContractReading_AndSortsAfterIt()
    {
        var meter = Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "E-101", Day, 100, null);
        meter.Record(ReadingKind.Handover, Day, 100, A, null);
        meter.Record(ReadingKind.Final, Day.AddDays(30), 200, A, null);
        meter.Record(ReadingKind.Periodic, Day.AddDays(31), 200, A, null, Day).IsSuccess.Should().BeTrue("số chốt kỳ sáng ngày người mới vào");

        meter.Record(ReadingKind.Handover, Day.AddDays(31), 205, B, null).IsSuccess.Should().BeTrue("5 kWh phòng trống không tính cho ai");

        meter.Latest!.Kind.Should().Be(ReadingKind.Handover);
        meter.Latest.Value.Should().Be(205);
        meter.LatestOnOrBefore(Day.AddDays(31))!.ContractId.Should().Be(B);
    }

    [Fact]
    public void Correct_PreviousContractReading_IsNotBoundedByNewContractReadings()
    {
        var meter = MeterWithTenantA();
        meter.Record(ReadingKind.Handover, Day.AddDays(41), 200, B, null);
        var final = meter.Readings.Single(r => r.Kind == ReadingKind.Final);

        meter.Correct(final.Id, 255, "gõ lại").IsSuccess.Should().BeTrue();
        meter.Correct(final.Id, 240, "gõ lại").Error!.Code.Should().Be("READING_NOT_MONOTONIC", "vẫn không nhỏ hơn số chốt kỳ 250 của chính HĐ A");
    }

    [Fact]
    public void Removal_StillMustNotBeBelowTheLatestReading()
    {
        var meter = MeterWithTenantA();
        meter.Record(ReadingKind.Handover, Day.AddDays(41), 200, B, null);

        meter.Remove(Day.AddDays(50), 190, null).Error!.Code.Should().Be("READING_NOT_MONOTONIC");
        meter.Remove(Day.AddDays(50), 210, null).IsSuccess.Should().BeTrue();
    }
}

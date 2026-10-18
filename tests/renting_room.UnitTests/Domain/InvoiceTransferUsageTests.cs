using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.UnitTests.Domain;

/// <summary>
/// Điện nước khi chuyển phòng / thay công tơ / phiếu quyết toán (17/10/2026): sản lượng phòng cũ cộng vào dòng của kỳ (M05 CT-BR-47),
/// mỗi kỳ sử dụng một dòng riêng (M07 BL-BR-05). Khu chốt ngày 5: kỳ tháng 10 = 05/10–04/11.
/// </summary>
public sealed class InvoiceTransferUsageTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly Guid PropertyId = Guid.NewGuid();
    private static readonly Guid RoomA = Guid.NewGuid();
    private static readonly Guid RoomB = Guid.NewGuid();
    private static readonly Guid Renter = Guid.NewGuid();

    private static FeeType Electricity(params PriceTier[] tiers)
    {
        var fee = FeeType.Create(PropertyId, "Điện", FeeGroup.Metered, null, "kWh", false, null, 0, FeeSystemCodes.Electricity);
        fee.AddPrice(new DateOnly(2026, 1, 1), tiers.Length == 0 ? 3800 : 0, null, null, tiers.Length == 0 ? null : tiers).IsSuccess.Should().BeTrue();
        return fee;
    }

    private static (Contract Contract, BillingSchedule Schedule) Active(ChargeMode mode)
    {
        var data = new ContractDraftData(Renter, Start, null, null, null, null, 3_000_000, 0, null, 30, [PaymentMethod.Cash], 2, null, null,
            [new OccupantInput(Renter, Start, null, null, null)], Fees: []);
        var contract = Contract.CreateDraft(PropertyId, RoomA, "HD2026-0009", data);
        contract.Activate(new ActivationContext(Start, DateTimeOffset.UtcNow, true, "{}", null)).IsSuccess.Should().BeTrue();
        return (contract, BillingSchedule.Single(5, mode));
    }

    private static Meter Install(Guid roomId, FeeType fee, decimal initial, string serial) =>
        Meter.Install(PropertyId, roomId, fee.Id, serial, Start.AddDays(-30), initial, null);

    private static void Record(Meter meter, ReadingKind kind, DateOnly date, decimal value, Contract contract, DateOnly? closing = null) =>
        meter.Record(kind, date, value, contract.Id, null, closing).IsSuccess.Should().BeTrue();

    private static InvoiceCalculation Calculate(
        Contract contract, BillingSchedule schedule, BillingPeriod period, FeeType fee, Meter[] meters,
        Dictionary<Guid, Guid>? lastEnd = null, FinalSettlement? final = null, IReadOnlySet<Guid>? closed = null) =>
        InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period, schedule, ProrationMode.Daily,
            new Dictionary<Guid, FeeType> { [fee.Id] = fee }, meters, lastEnd ?? [], final, ClosedReadingIds: closed));

    /// <summary>Chuyển A → B ngày 20/10: A 100 → 160 (chỉ số lúc chuyển) + B 500 → 540 ⇒ một dòng 100 kWh.</summary>
    private static (Contract, BillingSchedule, BillingPeriod, Meter A, Meter B) TransferMidPeriod(FeeType fee)
    {
        var (contract, schedule) = Active(ChargeMode.Postpaid);
        var period = contract.BillingPeriods(schedule, Start).First();
        var day = Start.AddDays(15);
        var meterA = Install(RoomA, fee, 100, "A");
        var meterB = Install(RoomB, fee, 500, "B");
        Record(meterA, ReadingKind.Handover, Start, 100, contract);
        Record(meterA, ReadingKind.Final, day, 160, contract);
        contract.TransferRoom(RoomB, day, day).IsSuccess.Should().BeTrue();
        Record(meterB, ReadingKind.Handover, day, 500, contract);
        Record(meterB, ReadingKind.Periodic, period.End, 540, contract, period.Start);
        return (contract, schedule, period, meterA, meterB);
    }

    [Fact]
    public void TransferMidPeriod_OneLine_OldRoomUntilTransferReading_PlusNewRoomFromHandover()
    {
        var fee = Electricity();
        var (contract, schedule, period, meterA, meterB) = TransferMidPeriod(fee);

        var result = Calculate(contract, schedule, period, fee, [meterA, meterB]);

        result.Issues.Should().BeEmpty();
        var metered = result.Lines.Single(l => l.Type == InvoiceLineType.Metered);
        metered.Quantity.Should().Be(60 + 40);
        metered.Amount.Should().Be(100 * 3800);
        result.Segments.Select(s => (s.MeterId, s.StartValue, s.EndValue)).Should().BeEquivalentTo([(meterA.Id, 100m, 160m), (meterB.Id, 500m, 540m)]);
    }

    [Fact]
    public void TransferMidPeriod_TieredPrice_TiersApplyOnTotalOfBothRooms()
    {
        var fee = Electricity(new PriceTier(50, 2000), new PriceTier(null, 3000));
        var (contract, schedule, period, meterA, meterB) = TransferMidPeriod(fee);

        var metered = Calculate(contract, schedule, period, fee, [meterA, meterB]).Lines.Single(l => l.Type == InvoiceLineType.Metered);

        metered.Quantity.Should().Be(100);
        metered.Amount.Should().Be(50 * 2000 + 50 * 3000, "bậc tính trên tổng 100 kWh của kỳ, không tính riêng 60 + 40");
    }

    [Fact]
    public void LegacyClosingCharge_SegmentNotBilledAgain()
    {
        var fee = Electricity();
        var (contract, schedule, period, meterA, meterB) = TransferMidPeriod(fee);
        var transferReading = meterA.FindFinal(contract.Id)!;

        var metered = Calculate(contract, schedule, period, fee, [meterA, meterB], closed: new HashSet<Guid> { transferReading.Id })
            .Lines.Single(l => l.Type == InvoiceLineType.Metered);

        metered.Quantity.Should().Be(40, "đoạn phòng cũ đã thu bằng khoản chốt tạo trước 17/10/2026");
    }

    [Fact]
    public void AwayAndBackInSamePeriod_EachStayIsItsOwnSegment_VacantGapNotBilled()
    {
        var fee = Electricity();
        var (contract, schedule) = Active(ChargeMode.Postpaid);
        var period = contract.BillingPeriods(schedule, Start).First();
        var meterA = Install(RoomA, fee, 100, "A");
        var meterB = Install(RoomB, fee, 500, "B");
        Record(meterA, ReadingKind.Handover, Start, 100, contract);
        var away = Start.AddDays(7);
        Record(meterA, ReadingKind.Final, away, 130, contract);
        contract.TransferRoom(RoomB, away, away).IsSuccess.Should().BeTrue();
        Record(meterB, ReadingKind.Handover, away, 500, contract);
        var back = Start.AddDays(17);
        Record(meterB, ReadingKind.Final, back, 515, contract);
        contract.TransferRoom(RoomA, back, back).IsSuccess.Should().BeTrue();
        Record(meterA, ReadingKind.Handover, back, 138, contract); // phòng trống thợ dùng 8 kWh
        Record(meterA, ReadingKind.Periodic, period.End, 160, contract, period.Start);

        var result = Calculate(contract, schedule, period, fee, [meterA, meterB]);

        result.Issues.Should().BeEmpty();
        result.Lines.Single(l => l.Type == InvoiceLineType.Metered).Quantity.Should().Be(30 + 15 + 22);
        result.Segments.Should().HaveCount(3);
        meterA.FindFinal(contract.Id, Start, away)!.Value.Should().Be(130, "chỉ số cuối của lần ở đầu, không lấy của lần ở sau");
    }

    [Fact]
    public void MeterReplacedInOldRoom_ThenTransfer_OldMeterSegmentIncluded()
    {
        var fee = Electricity();
        var (contract, schedule) = Active(ChargeMode.Postpaid);
        var period = contract.BillingPeriods(schedule, Start).First();
        var oldMeter = Install(RoomA, fee, 100, "A-1");
        var meterB = Install(RoomB, fee, 500, "B");
        Record(oldMeter, ReadingKind.Handover, Start, 100, contract);
        var newMeter = oldMeter.ReplaceWith(Start.AddDays(5), 120, "A-2", 0, null).Value!;
        var day = Start.AddDays(15);
        Record(newMeter, ReadingKind.Final, day, 30, contract);
        contract.TransferRoom(RoomB, day, day).IsSuccess.Should().BeTrue();
        Record(meterB, ReadingKind.Handover, day, 500, contract);
        Record(meterB, ReadingKind.Periodic, period.End, 540, contract, period.Start);

        var metered = Calculate(contract, schedule, period, fee, [oldMeter, newMeter, meterB]).Lines.Single(l => l.Type == InvoiceLineType.Metered);

        metered.Quantity.Should().Be(20 + 30 + 40, "công tơ cũ đã thay (100 → 120) không bị mất khi chuyển phòng");
    }

    [Fact]
    public void Prepaid_TransferAfterUsagePeriod_NoPeriodicOnOldRoom_UsesTransferReading_NextPeriodDoesNotBillAgain()
    {
        var fee = Electricity();
        var (contract, schedule) = Active(ChargeMode.Prepaid);
        var meterA = Install(RoomA, fee, 100, "A");
        var meterB = Install(RoomB, fee, 500, "B");
        Record(meterA, ReadingKind.Handover, Start, 100, contract);
        var day = Start.AddMonths(1).AddDays(5); // 10/11 — kỳ tháng 11, chưa ghi chỉ số cuối kỳ tháng 10 của phòng cũ
        Record(meterA, ReadingKind.Final, day, 180, contract);
        contract.TransferRoom(RoomB, day, day).IsSuccess.Should().BeTrue();
        Record(meterB, ReadingKind.Handover, day, 500, contract);
        var periods = contract.BillingPeriods(schedule, Start.AddMonths(2));

        var november = Calculate(contract, schedule, periods[1], fee, [meterA, meterB]);
        november.Issues.Should().NotContain(i => i.Code == "MISSING_READING");
        november.Lines.Single(l => l.Type == InvoiceLineType.Metered).Quantity.Should().Be(80, "phòng cũ tính tới chỉ số lúc chuyển");

        Record(meterB, ReadingKind.Periodic, periods[1].End, 530, contract, periods[1].Start);
        var lastEnd = november.Segments.ToDictionary(s => s.MeterId, s => s.EndReadingId);
        var december = Calculate(contract, schedule, periods[2], fee, [meterA, meterB], lastEnd);
        december.Lines.Single(l => l.Type == InvoiceLineType.Metered).Quantity.Should().Be(30, "phòng cũ đã tính hết ở kỳ trước");
        december.Segments.Should().ContainSingle(s => s.MeterId == meterB.Id);
    }

    [Fact]
    public void FinalInvoice_Prepaid_PreviousPeriodUnbilled_TwoLines_TiersPerPeriod()
    {
        var fee = Electricity(new PriceTier(50, 1800), new PriceTier(100, 2300), new PriceTier(null, 3000));
        var (contract, schedule) = Active(ChargeMode.Prepaid);
        var meter = Install(RoomA, fee, 100, "A");
        Record(meter, ReadingKind.Handover, Start, 100, contract);
        var moveOut = Start.AddMonths(1).AddDays(15); // 20/11
        contract.StartLiquidation(moveOut, TerminationReason.MutualAgreement, null, null, moveOut).IsSuccess.Should().BeTrue();
        Record(meter, ReadingKind.Final, moveOut, 220, contract);
        var periods = contract.BillingPeriods(schedule, moveOut);
        var final = new FinalSettlement(RentBilled: null, LastPeriodHasRegular: false);

        var missing = Calculate(contract, schedule, periods[^1], fee, [meter], final: final);
        missing.Issues.Should().Contain(i => i.Code == "MISSING_READING", "mốc giữa 2 kỳ là chỉ số cuối kỳ tháng 10");

        Record(meter, ReadingKind.Periodic, periods[0].End, 180, contract, periods[0].Start);
        var result = Calculate(contract, schedule, periods[^1], fee, [meter], final: final);

        result.Issues.Should().NotContain(i => i.Severity == InvoiceIssue.Error);
        var lines = result.Lines.Where(l => l.Type == InvoiceLineType.Metered).ToList();
        lines.Select(l => (l.Quantity, l.Amount)).Should().Equal((80m, 50 * 1800m + 30 * 2300m), (40m, 40 * 1800m));
        lines[0].Description.Should().Contain("05/10–04/11");
        lines[1].ServiceFrom.Should().Be(periods[^1].Start);
    }
}

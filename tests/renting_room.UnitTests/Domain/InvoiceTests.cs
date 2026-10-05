using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.UnitTests.Domain;

public sealed class InvoiceTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly Guid PropertyId = Guid.NewGuid();
    private static readonly Guid RoomId = Guid.NewGuid();
    private static readonly Guid RenterA = Guid.NewGuid();
    private static readonly Guid RenterB = Guid.NewGuid();

    private static readonly FeeType Electricity = Priced(FeeType.Create(PropertyId, "Điện", FeeGroup.Metered, null, "kWh", false, null, 0, FeeSystemCodes.Electricity), 3800);
    private static readonly FeeType WaterPerPerson = Priced(FeeType.Create(PropertyId, "Nước", FeeGroup.Service, ChargeBasis.PerOccupant, "người", false, null, 1), 20000);
    private static readonly FeeType Parking = Priced(FeeType.Create(PropertyId, "Giữ xe", FeeGroup.Service, ChargeBasis.PerUnit, "xe", false, 1, 2), 100000);

    private static FeeType Priced(FeeType fee, decimal price)
    {
        fee.AddPrice(new DateOnly(2026, 1, 1), price, null, null);
        return fee;
    }

    private static Contract Active(ChargeMode mode, DateOnly? start = null, int rentCycleMonths = 1, DateOnly? endDate = null)
    {
        var from = start ?? Start;
        var data = new ContractDraftData(RenterA, from, endDate, null, null, null, 3_500_000, 0, null, 5, mode, ProrationMode.Daily, 5, 30,
            [PaymentMethod.Cash], 2, null, null,
            [new OccupantInput(RenterA, from, null, null, null), new OccupantInput(RenterB, from, null, null, null, OccupantRelationship.CoTenant)],
            Fees: [new ContractFeeInput(WaterPerPerson.Id, 1, null), new ContractFeeInput(Parking.Id, 2, null)], RentCycleMonths: rentCycleMonths);
        var contract = Contract.CreateDraft(PropertyId, RoomId, "HD2026-0001", data);
        contract.Activate(new ActivationContext(from, DateTimeOffset.UtcNow, true, 4, true, "{}", null, new DateOnly(1990, 1, 1), true))
            .IsSuccess.Should().BeTrue();
        return contract;
    }

    private static Meter MeterWithHandover(Contract contract, decimal handover)
    {
        var meter = Meter.Install(PropertyId, RoomId, Electricity.Id, "E-101", contract.StartDate.AddDays(-30), 100, null);
        meter.Record(ReadingKind.Handover, contract.StartDate, handover, contract.Id, null).IsSuccess.Should().BeTrue();
        return meter;
    }

    private static InvoiceCalculation Calculate(Contract contract, BillingPeriod period, params Meter[] meters) =>
        InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period,
            new Dictionary<Guid, FeeType> { [Electricity.Id] = Electricity, [WaterPerPerson.Id] = WaterPerPerson, [Parking.Id] = Parking },
            meters, new Dictionary<Guid, Guid>()));

    [Theory]
    [InlineData("2026-10-03", "2026-11-04", 2.0 / 30 + 1)]  // kỳ gộp: 2/30 của kỳ chuẩn 05/09–04/10 + trọn kỳ 05/10–04/11
    [InlineData("2026-11-05", "2026-12-04", 1.0)]
    [InlineData("2026-11-05", "2026-11-19", 15.0 / 30)]     // trả phòng giữa kỳ (kỳ chuẩn 05/11–04/12 có 30 ngày)
    public void ProrationFactor_FollowsStandardPeriods(string from, string to, double expected)
    {
        BillingPeriodCalculator.ProrationFactor(DateOnly.Parse(from), DateOnly.Parse(to), 5)
            .Should().BeApproximately((decimal)expected, 0.000001m);
    }

    [Fact]
    public void Postpaid_RentServicesAndElectricity_FromHandoverToPeriodEnd()
    {
        var contract = Active(ChargeMode.Postpaid);
        var meter = MeterWithHandover(contract, 108);
        var period = contract.BillingPeriods(Start).First();
        meter.Record(ReadingKind.Periodic, period.End, 196, contract.Id, null, period.Start).IsSuccess.Should().BeTrue();

        var result = Calculate(contract, period, meter);

        result.Issues.Should().BeEmpty();
        result.Lines.Select(l => (l.Type, l.Description, l.Quantity, l.Amount)).Should().Equal(
            (InvoiceLineType.Rent, "Tiền phòng", 1m, 3_500_000m),
            (InvoiceLineType.Metered, "Điện", 88m, 88 * 3800m),
            (InvoiceLineType.Service, "Nước", 2m, 40_000m),
            (InvoiceLineType.Service, "Giữ xe", 2m, 200_000m));
        result.Segments.Should().ContainSingle(s => s.StartValue == 108 && s.EndValue == 196);
    }

    [Fact]
    public void Prepaid_FirstPeriod_HasNoElectricity_SecondBillsPreviousPeriod()
    {
        var contract = Active(ChargeMode.Prepaid);
        var meter = MeterWithHandover(contract, 108);
        var periods = contract.BillingPeriods(Start.AddMonths(1));

        Calculate(contract, periods[0], meter).Lines.Should().NotContain(l => l.Type == InvoiceLineType.Metered);

        var missing = Calculate(contract, periods[1], meter);
        missing.Issues.Should().ContainSingle(i => i.Code == "MISSING_READING");

        meter.Record(ReadingKind.Periodic, periods[0].End, 150, contract.Id, null, periods[0].Start);
        Calculate(contract, periods[1], meter).Lines.Single(l => l.Type == InvoiceLineType.Metered)
            .Should().Match<CalculatedLine>(l => l.Quantity == 42 && l.ServiceFrom == periods[0].Start && l.ServiceTo == periods[0].End);
    }

    [Fact]
    public void MeterReplacedMidPeriod_AddsOldMeterUsage_AndUsesNewPrice()
    {
        var contract = Active(ChargeMode.Postpaid);
        var oldMeter = MeterWithHandover(contract, 1250);
        var period = contract.BillingPeriods(Start).First();
        var newMeter = oldMeter.ReplaceWith(Start.AddDays(10), 1320, "E-102", 0, null).Value!;
        newMeter.Record(ReadingKind.Periodic, period.End, 45, contract.Id, null, period.Start);
        Electricity.AddPrice(Start.AddDays(20), 4000, "Tăng giá", null); // đổi giữa kỳ ⇒ cả kỳ tính giá mới

        var metered = Calculate(contract, period, oldMeter, newMeter).Lines.Single(l => l.Type == InvoiceLineType.Metered);

        metered.Quantity.Should().Be(70 + 45);
        metered.UnitPrice.Should().Be(4000);
        Electricity.RemovePrice(Electricity.Prices.Single(p => p.UnitPrice == 4000).Id, null);
    }

    [Fact]
    public void ManualEdit_KeptOnRecalculate_WarnsWhenSystemValueChanges_ResetRestores()
    {
        var contract = Active(ChargeMode.Postpaid);
        var period = contract.BillingPeriods(Start).First();
        var meter = MeterWithHandover(contract, 108);
        meter.Record(ReadingKind.Periodic, period.End, 196, contract.Id, null, period.Start);
        var invoice = Invoice.CreateDraft(PropertyId, RoomId, contract.Id, period.Start, period.End, "101", "HD", "A", Calculate(contract, period, meter));
        var rent = invoice.Lines.Single(l => l.Type == InvoiceLineType.Rent);

        invoice.EditLine(rent.Id, null, null, 3_000_000, null).Error.Should().Be(BillingErrors.NoteRequired);
        invoice.EditLine(rent.Id, null, null, 3_000_000, "Mất nước 5 ngày").IsSuccess.Should().BeTrue();
        invoice.AddManualLine(InvoiceLineType.Surcharge, "Thay khóa", null, null, 250_000, "Làm hỏng khóa", null).IsSuccess.Should().BeTrue();
        var total = invoice.TotalAmount;

        invoice.ApplyCalculation(Calculate(contract, period, meter), keepManualEdits: true);
        invoice.TotalAmount.Should().Be(total, "giữ ô sửa tay và phụ thu");
        invoice.Issues.Should().BeEmpty();
        invoice.SystemPartMatches(Calculate(contract, period, meter)).Should().BeTrue("so phần hệ thống, bỏ qua ô sửa tay");

        contract.OccupantsOn(period.Start).Should().HaveCount(2);
        invoice.ResetLine(rent.Id).IsSuccess.Should().BeTrue();
        rent.Amount.Should().Be(3_500_000);
        rent.IsManuallyEdited.Should().BeFalse();
    }

    [Fact]
    public void Finalize_BlockedByIssues_Void_BlockedByPayments()
    {
        var contract = Active(ChargeMode.Postpaid);
        var period = contract.BillingPeriods(Start).First();
        var meter = MeterWithHandover(contract, 108);
        var invoice = Invoice.CreateDraft(PropertyId, RoomId, contract.Id, period.Start, period.End, "101", "HD", "A", Calculate(contract, period, meter));

        invoice.Finalize("PB2026-000001", Start, 5, DateTimeOffset.UtcNow).Error!.Code.Should().Be("INVOICE_HAS_ISSUES");

        meter.Record(ReadingKind.Periodic, period.End, 196, contract.Id, null, period.Start);
        invoice.ApplyCalculation(Calculate(contract, period, meter), keepManualEdits: true);
        invoice.Finalize("PB2026-000001", Start, 5, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
        invoice.DueDate.Should().Be(Start.AddDays(5));
        invoice.EditLine(invoice.Lines[0].Id, 1, 1, 1, "x").Error.Should().Be(BillingErrors.NotDraft);

        invoice.ApplyPayment(1_000_000);
        invoice.PaymentStatus(Start).Should().Be(InvoicePaymentStatus.PartiallyPaid);
        invoice.Void("Sai", DateTimeOffset.UtcNow).Error.Should().Be(BillingErrors.HasPayments);
        invoice.ApplyPayment(-1_000_000);
        invoice.Void("Sai", DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
        invoice.Segments.Should().OnlyContain(s => s.Voided);
    }

    [Theory]
    [InlineData(40, 40 * 2000)]
    [InlineData(88, 50 * 2000 + 38 * 3000)]
    [InlineData(0, 0)]
    public void TieredPrice_ChargesEachSliceAtItsRate(int kWh, int expected)
    {
        var fee = FeeType.Create(PropertyId, "Điện bậc", FeeGroup.Metered, null, "kWh", false, null, 0);
        var price = fee.AddPrice(Start, 0, null, null, [new PriceTier(50, 2000), new PriceTier(null, 3000)]).Value!;

        price.IsTiered.Should().BeTrue();
        price.UnitPrice.Should().Be(2000, "đơn giá hiển thị = giá bậc 1");
        price.Amount(kWh).Should().Be(expected);
        price.HighestPrice.Should().Be(3000);
        WaterPerPerson.AddPrice(Start.AddDays(3), 0, null, null, [new PriceTier(null, 1)]).Error.Should().Be(FeeErrors.TiersForMeteredOnly);
    }

    [Fact]
    public void RentCycle_CutAtEndDate_WhenContractEndsMidCycle()
    {
        var contract = Active(ChargeMode.Prepaid, rentCycleMonths: 3, endDate: Start.AddMonths(2).AddDays(-1)); // HĐ 2 tháng, chu kỳ 3 tháng
        var rent = Calculate(contract, contract.BillingPeriods(Start).First()).Lines.Single(l => l.Type == InvoiceLineType.Rent);

        rent.Quantity.Should().Be(2, "chu kỳ cuối chỉ tính tới ngày hết hạn");
        rent.Amount.Should().Be(7_000_000);
    }

    [Fact]
    public void FinalInvoice_LastPeriodNotBilled_ChargesRentForDaysStayed_ServicesProrated()
    {
        var contract = Active(ChargeMode.Postpaid);
        contract.StartLiquidation(Start.AddDays(14), TerminationReason.MutualAgreement, null, null, Start.AddDays(14)).IsSuccess.Should().BeTrue();
        var meter = MeterWithHandover(contract, 108);
        meter.Record(ReadingKind.Final, Start.AddDays(14), 130, contract.Id, null);
        var period = contract.BillingPeriods(contract.ActualEndDate!.Value).Last();

        var result = InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period,
            new Dictionary<Guid, FeeType> { [Electricity.Id] = Electricity, [WaterPerPerson.Id] = WaterPerPerson, [Parking.Id] = Parking },
            [meter], new Dictionary<Guid, Guid>(), new FinalSettlement(RentCovered: false, LastPeriodHasRegular: false)));

        var factor = 15m / 31; // 05/10–19/10 trong kỳ chuẩn 05/10–04/11
        result.Lines.Single(l => l.Type == InvoiceLineType.Rent).Amount.Should().Be(Invoice.Money(3_500_000 * factor));
        result.Lines.Single(l => l.Type == InvoiceLineType.Metered).Quantity.Should().Be(22, "từ chỉ số nhận phòng tới chỉ số cuối");
        result.Lines.Should().Contain(l => l.Type == InvoiceLineType.Service);

        var covered = InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period,
            new Dictionary<Guid, FeeType> { [Electricity.Id] = Electricity, [WaterPerPerson.Id] = WaterPerPerson, [Parking.Id] = Parking },
            [meter], new Dictionary<Guid, Guid>(), new FinalSettlement(RentCovered: true, LastPeriodHasRegular: true)));
        covered.Lines.Select(l => l.Type).Should().Equal(InvoiceLineType.Metered); // tiền phòng, dịch vụ đã thu — không hoàn (để sau)
    }
}

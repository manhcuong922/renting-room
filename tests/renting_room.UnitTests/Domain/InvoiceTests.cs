using System.Runtime.CompilerServices;
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

    // Cách thu / ngày chốt là của khu (PR-BR-09) — test gắn lịch kỳ thu cho từng HĐ dựng sẵn.
    private static readonly ConditionalWeakTable<Contract, BillingSchedule> Schedules = new();

    private static BillingSchedule ScheduleOf(Contract contract) => Schedules.TryGetValue(contract, out var s) ? s : BillingSchedule.Single(5, ChargeMode.Postpaid);

    private static IReadOnlyList<BillingPeriod> Periods(Contract contract, DateOnly until) => contract.BillingPeriods(ScheduleOf(contract), until);

    private static readonly FeeType Electricity = Priced(FeeType.Create(PropertyId, "Điện", FeeGroup.Metered, null, "kWh", false, null, 0, FeeSystemCodes.Electricity), 3800);
    private static readonly FeeType WaterPerPerson = Priced(FeeType.Create(PropertyId, "Nước", FeeGroup.Service, ChargeBasis.PerOccupant, "người", false, null, 1), 20000);
    private static readonly FeeType Parking = Priced(FeeType.Create(PropertyId, "Giữ xe", FeeGroup.Service, ChargeBasis.PerUnit, "xe", false, 1, 2), 100000);

    private static FeeType Priced(FeeType fee, decimal price)
    {
        fee.AddPrice(new DateOnly(2026, 1, 1), price, null, null);
        return fee;
    }

    private static Contract Active(ChargeMode mode, DateOnly? start = null, DateOnly? endDate = null, IReadOnlyCollection<ContractFeeInput>? fees = null)
    {
        var from = start ?? Start;
        var data = new ContractDraftData(RenterA, from, endDate, null, null, null, 3_500_000, 0, null, 30,
            [PaymentMethod.Cash], 2, null, null,
            [new OccupantInput(RenterA, from, null, null, null), new OccupantInput(RenterB, from, null, null, null, OccupantRelationship.CoTenant)],
            Fees: fees ?? [new ContractFeeInput(WaterPerPerson.Id, 1, null), new ContractFeeInput(Parking.Id, 2, null)]);
        var contract = Contract.CreateDraft(PropertyId, RoomId, "HD2026-0001", data);
        contract.Activate(new ActivationContext(from, DateTimeOffset.UtcNow, true, "{}", null))
            .IsSuccess.Should().BeTrue();
        Schedules.AddOrUpdate(contract, BillingSchedule.Single(5, mode));
        return contract;
    }

    private static Meter MeterWithHandover(Contract contract, decimal handover)
    {
        var meter = Meter.Install(PropertyId, RoomId, Electricity.Id, "E-101", contract.StartDate.AddDays(-30), 100, null);
        meter.Record(ReadingKind.Handover, contract.StartDate, handover, contract.Id, null).IsSuccess.Should().BeTrue();
        return meter;
    }

    private static InvoiceCalculation Calculate(Contract contract, BillingPeriod period, params Meter[] meters) =>
        InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period, ScheduleOf(contract), ProrationMode.Daily,
            new Dictionary<Guid, FeeType> { [Electricity.Id] = Electricity, [WaterPerPerson.Id] = WaterPerPerson, [Parking.Id] = Parking },
            meters, new Dictionary<Guid, Guid>()));

    [Fact]
    public void Postpaid_RentServicesAndElectricity_FromHandoverToPeriodEnd()
    {
        var contract = Active(ChargeMode.Postpaid);
        var meter = MeterWithHandover(contract, 108);
        var period = Periods(contract, Start).First();
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
        var periods = Periods(contract, Start.AddMonths(1));

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
        var period = Periods(contract, Start).First();
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
        var period = Periods(contract, Start).First();
        var meter = MeterWithHandover(contract, 108);
        meter.Record(ReadingKind.Periodic, period.End, 196, contract.Id, null, period.Start);
        var invoice = Invoice.CreateDraft(PropertyId, RoomId, contract.Id, period, "101", "HD", "A", Calculate(contract, period, meter));
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
        var period = Periods(contract, Start).First();
        var meter = MeterWithHandover(contract, 108);
        var invoice = Invoice.CreateDraft(PropertyId, RoomId, contract.Id, period, "101", "HD", "A", Calculate(contract, period, meter));

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
    public void ServicePrice_UsesLatestVersionUpToPeriodEnd_ForWholePeriod()
    {
        var wifi = FeeType.Create(PropertyId, "Wifi", FeeGroup.Service, ChargeBasis.PerRoom, "phòng", false, null, 3);
        wifi.AddPrice(new DateOnly(2026, 1, 1), 100_000, null, null);
        wifi.AddPrice(Start.AddDays(20), 120_000, null, null); // tăng giá giữa kỳ 05/10–04/11
        var contract = Active(ChargeMode.Prepaid, fees: [new ContractFeeInput(wifi.Id, 1, null)]);

        var result = InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, Periods(contract, Start).First(), ScheduleOf(contract), ProrationMode.Daily,
            new Dictionary<Guid, FeeType> { [wifi.Id] = wifi }, [], new Dictionary<Guid, Guid>()));

        var line = result.Lines.Single(l => l.Type == InvoiceLineType.Service);
        line.UnitPrice.Should().Be(120_000, "giá theo phiên bản đang áp dụng tới cuối kỳ, không chia nửa kỳ");
        line.Amount.Should().Be(120_000);
    }

    [Fact]
    public void Services_AreFullMonth_EvenInAPartialFirstPeriod()
    {
        var contract = Active(ChargeMode.Postpaid, start: new DateOnly(2026, 10, 25)); // kỳ đầu 25/10–04/11 (11 ngày)
        var period = Periods(contract, contract.StartDate).First();

        var lines = Calculate(contract, period).Lines;

        lines.Single(l => l.Type == InvoiceLineType.Rent).Amount.Should().Be(Invoice.Money(3_500_000m * 11 / 31));
        lines.Where(l => l.Type == InvoiceLineType.Service).Sum(l => l.Amount)
            .Should().Be(2 * 20_000 + 2 * 100_000, "BL-BR-04: dịch vụ không chia theo ngày — sửa tay trên nháp nếu cần");
    }

    [Fact]
    public void TransitionPeriod_ChargesOneMonthPlusChosenDays_ServicesStayOneMonth()
    {
        var contract = Active(ChargeMode.Postpaid);
        // K4: khu chốt ngày 5 đổi sang ngày 10 từ 05/11 ⇒ kỳ chuyển tiếp 05/11–09/12 dư 5 ngày, gợi ý tính đủ 5 ngày.
        Schedules.AddOrUpdate(contract, BillingSchedule.Single(5, ChargeMode.Postpaid).ChangeFrom(new DateOnly(2026, 11, 5), 10, ChargeMode.Postpaid, null));
        var transition = Periods(contract, new DateOnly(2026, 11, 5))[1];

        var lines = Calculate(contract, transition).Lines;

        transition.Should().Be(new BillingPeriod(new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 9), new DateOnly(2026, 11, 1)));
        var rent = lines.Single(l => l.Type == InvoiceLineType.Rent);
        rent.Amount.Should().Be(Invoice.Money(3_500_000m * (1 + 5m / 30)));
        rent.Description.Should().Contain("+ 5 ngày");
        lines.Where(l => l.Type == InvoiceLineType.Service).Sum(l => l.Amount).Should().Be(2 * 20_000 + 2 * 100_000);
    }

    [Fact]
    public void PostpaidToPrepaid_FirstPrepaidInvoice_SkipsMeteredAlreadyBilled_AndWarnsTwoRentMonths()
    {
        var contract = Active(ChargeMode.Postpaid);
        Schedules.AddOrUpdate(contract, BillingSchedule.Single(5, ChargeMode.Postpaid).ChangeFrom(new DateOnly(2026, 11, 5), 5, ChargeMode.Prepaid, null));
        var meter = MeterWithHandover(contract, 100);
        var periods = Periods(contract, new DateOnly(2026, 11, 5));

        var result = Calculate(contract, periods[1], meter);

        result.Lines.Should().NotContain(l => l.Type == InvoiceLineType.Metered, "điện nước tháng 10 đã thu trên phiếu thu sau tháng 10");
        result.Issues.Should().Contain(i => i.Code == "TWO_RENT_PERIODS" && i.Severity == InvoiceIssue.Warning);
    }

    [Fact]
    public void PrepaidToPostpaid_FirstPostpaidInvoice_IncludesPreviousPeriodUsage()
    {
        var contract = Active(ChargeMode.Prepaid);
        Schedules.AddOrUpdate(contract, BillingSchedule.Single(5, ChargeMode.Prepaid).ChangeFrom(new DateOnly(2026, 11, 5), 5, ChargeMode.Postpaid, null));
        var meter = MeterWithHandover(contract, 100);
        var periods = Periods(contract, new DateOnly(2026, 11, 5));
        meter.Record(ReadingKind.Periodic, periods[1].End, 180, contract.Id, null, periods[1].Start).IsSuccess.Should().BeTrue();

        var metered = Calculate(contract, periods[1], meter).Lines.Single(l => l.Type == InvoiceLineType.Metered);

        metered.ServiceFrom.Should().Be(periods[0].Start, "phiếu thu trước tháng 10 chưa thu điện nước tháng 10");
        metered.Quantity.Should().Be(80);
    }

    [Fact]
    public void Rent_IsMonthlyOnly_OneLinePerPeriod()
    {
        var contract = Active(ChargeMode.Prepaid);
        var periods = Periods(contract, Start.AddMonths(2)).ToList();

        foreach (var period in periods.Take(3))
            Calculate(contract, period).Lines.Single(l => l.Type == InvoiceLineType.Rent).Amount.Should().Be(3_500_000);
    }

    [Fact]
    public void FinalInvoice_LastPeriodNotBilled_ChargesRentForDaysStayed_ServicesFullMonth()
    {
        var contract = Active(ChargeMode.Postpaid);
        contract.StartLiquidation(Start.AddDays(14), TerminationReason.MutualAgreement, null, null, Start.AddDays(14)).IsSuccess.Should().BeTrue();
        var meter = MeterWithHandover(contract, 108);
        meter.Record(ReadingKind.Final, Start.AddDays(14), 130, contract.Id, null);
        var period = Periods(contract, contract.ActualEndDate!.Value).Last();

        var result = InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period, ScheduleOf(contract), ProrationMode.Daily,
            new Dictionary<Guid, FeeType> { [Electricity.Id] = Electricity, [WaterPerPerson.Id] = WaterPerPerson, [Parking.Id] = Parking },
            [meter], new Dictionary<Guid, Guid>(), new FinalSettlement(RentBilled: null, LastPeriodHasRegular: false)));

        var factor = 15m / 31; // 05/10–19/10 trong kỳ chuẩn 05/10–04/11
        result.Lines.Single(l => l.Type == InvoiceLineType.Rent).Amount.Should().Be(Invoice.Money(3_500_000 * factor));
        result.Lines.Single(l => l.Type == InvoiceLineType.Metered).Quantity.Should().Be(22, "từ chỉ số nhận phòng tới chỉ số cuối");
        result.Lines.Where(l => l.Type == InvoiceLineType.Service).Sum(l => l.Amount)
            .Should().Be(2 * 20_000 + 2 * 100_000, "dịch vụ thu trọn tháng kể cả phiếu quyết toán (BL-BR-17, L1)");

        var covered = InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period, ScheduleOf(contract), ProrationMode.Daily,
            new Dictionary<Guid, FeeType> { [Electricity.Id] = Electricity, [WaterPerPerson.Id] = WaterPerPerson, [Parking.Id] = Parking },
            [meter], new Dictionary<Guid, Guid>(), new FinalSettlement(RentBilled: 3_500_000, LastPeriodHasRegular: true)));
        covered.Lines.Select(l => l.Type).Should().Equal(InvoiceLineType.Metered); // tiền phòng, dịch vụ đã thu — không tự hoàn
        var overpaid = 3_500_000 - Invoice.Money(3_500_000 * factor);
        covered.Issues.Should().ContainSingle(i => i.Code == "RENT_OVERPAID" && i.Severity == InvoiceIssue.Warning)
            .Which.Message.Should().Contain($"{overpaid:N0}");
    }

    private static Invoice Draft(decimal rent = 1_000_000) =>
        Invoice.CreateDraft(PropertyId, RoomId, Guid.NewGuid(), new BillingPeriod(Start, Start.AddMonths(1).AddDays(-1), new DateOnly(Start.Year, Start.Month, 1)), "101", "HD2026-0001", "A",
            new InvoiceCalculation([new CalculatedLine(InvoiceLineType.Rent, null, "Tiền phòng", "tháng", Start, Start.AddMonths(1).AddDays(-1),
                1, rent, 1, rent, 0)], [], []));

    [Fact]
    public void Refund_CanMakeTotalNegative_DiscountCannotExceedCharges()
    {
        var invoice = Draft();

        invoice.AddManualLine(InvoiceLineType.ManualDiscount, "Giảm", null, null, 1_000_001, "Lý do", null).Error
            .Should().Be(BillingErrors.NegativeTotal, "giảm trừ không vượt phần thu");
        invoice.AddManualLine(InvoiceLineType.Refund, "Hoàn tiền phòng chưa ở", null, null, 1_500_000, "Trả phòng sớm", null)
            .IsSuccess.Should().BeTrue();

        invoice.Lines.Single(l => l.Type == InvoiceLineType.Refund).Amount.Should().Be(-1_500_000);
        invoice.RefundTotal.Should().Be(-1_500_000);
        invoice.DiscountTotal.Should().Be(0);
        invoice.TotalAmount.Should().Be(-500_000);
        invoice.AddManualLine(InvoiceLineType.Refund, "Hoàn", null, null, 1, null, null).Error.Should().Be(BillingErrors.NoteRequired);
    }

    [Fact]
    public void NegativeInvoice_Finalized_IsRefundPending_UntilConfirmed_AndNotDebt()
    {
        var invoice = Draft();
        invoice.AddManualLine(InvoiceLineType.Refund, "Hoàn cọc giữ chỗ", null, null, 1_200_000, "Trả phòng", null);
        var today = Start.AddMonths(1);
        invoice.Finalize("PB2026-000001", today, 5, DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();

        invoice.PaymentStatus(today).Should().Be(InvoicePaymentStatus.RefundPending);
        invoice.Outstanding.Should().Be(0, "phiếu âm không phải nợ");
        invoice.RefundDue.Should().Be(200_000);
        invoice.ConfirmRefund(today.AddDays(1), PaymentMethod.Cash, null, today).Error.Should().Be(BillingErrors.InvalidRefundDate);

        invoice.ConfirmRefund(today, PaymentMethod.BankTransfer, "CK Vietcombank", today).IsSuccess.Should().BeTrue();
        invoice.PaymentStatus(today).Should().Be(InvoicePaymentStatus.Refunded);
        invoice.RefundDue.Should().Be(0);
        invoice.Void("Sai", DateTimeOffset.UtcNow).Error.Should().Be(BillingErrors.RefundConfirmed);

        invoice.CancelRefund().IsSuccess.Should().BeTrue();
        invoice.Void("Sai", DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ConfirmRefund_RejectedWhenNothingToRefund()
    {
        var invoice = Draft();
        invoice.Finalize("PB2026-000002", Start, 5, DateTimeOffset.UtcNow);

        invoice.ConfirmRefund(Start, PaymentMethod.Cash, null, Start).Error.Should().Be(BillingErrors.NothingToRefund);
        invoice.PaymentStatus(Start).Should().Be(InvoicePaymentStatus.Unpaid);
    }
}

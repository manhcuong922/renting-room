using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.UnitTests;

/// <summary>
/// M-04 (11/11/2026, M07 BL-BR-07): sửa tay SỐ LƯỢNG dòng điện nước giá bậc phải chia bậc lại theo sản lượng mới. Trước đây
/// = số lượng mới × giá bình quân của sản lượng cũ ⇒ thiếu / thừa tiền khi sản lượng đổi qua ranh giới bậc.
/// Giá bậc: 0–50 kWh 1.800 · 51–100 kWh 2.200 · trên 100 kWh 2.800.
/// </summary>
public sealed class InvoiceTieredLineEditTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly Guid PropertyId = Guid.NewGuid();
    private static readonly Guid RoomId = Guid.NewGuid();
    private static readonly Guid Renter = Guid.NewGuid();

    private static (Invoice Invoice, InvoiceLine Line) EightyKwhInvoice(params PriceTier[] tiers)
    {
        var fee = FeeType.Create(PropertyId, "Điện", FeeGroup.Metered, null, "kWh", false, null, 0, FeeSystemCodes.Electricity);
        fee.AddPrice(new DateOnly(2026, 1, 1), tiers.Length == 0 ? 3800 : 0, null, null, tiers.Length == 0 ? null : tiers).IsSuccess.Should().BeTrue();
        var data = new ContractDraftData(Renter, Start, null, null, null, null, 3_000_000, 0, null, 30, [PaymentMethod.Cash], 2, null, null,
            [new OccupantInput(Renter, Start, null, null, null)], Fees: []);
        var contract = Contract.CreateDraft(PropertyId, RoomId, "HD2026-0004", data);
        contract.Activate(new ActivationContext(Start, DateTimeOffset.UtcNow, true, "{}", null)).IsSuccess.Should().BeTrue();
        var schedule = BillingSchedule.Single(5, ChargeMode.Postpaid);
        var period = contract.BillingPeriods(schedule, Start).First();

        var meter = Meter.Install(PropertyId, RoomId, fee.Id, "E-1", Start.AddDays(-30), 100, null);
        meter.Record(ReadingKind.Handover, Start, 100, contract.Id, null).IsSuccess.Should().BeTrue();
        meter.Record(ReadingKind.Periodic, period.End, 180, contract.Id, null, period.Start).IsSuccess.Should().BeTrue(); // 80 kWh

        var calculation = InvoiceCalculator.Calculate(new InvoiceCalcInput(contract, period, schedule, ProrationMode.Daily,
            new Dictionary<Guid, FeeType> { [fee.Id] = fee }, [meter], new Dictionary<Guid, Guid>()));
        var invoice = Invoice.CreateDraft(PropertyId, RoomId, contract.Id, period, "101", "HD", "A", calculation);
        return (invoice, invoice.Lines.Single(l => l.Type == InvoiceLineType.Metered));
    }

    private static readonly PriceTier[] Tiers = [new(50, 1800), new(100, 2200), new(null, 2800)];

    [Fact]
    public void EditQuantity_OnTieredLine_SplitsTiersAgain_NotAverageOfOldQuantity()
    {
        var (invoice, line) = EightyKwhInvoice(Tiers);
        line.Amount.Should().Be(50 * 1800 + 30 * 2200, "80 kWh = 90.000 + 66.000");

        invoice.EditLine(line.Id, 120, null, null, null).IsSuccess.Should().BeTrue();

        line.Quantity.Should().Be(120);
        line.Amount.Should().Be(50 * 1800 + 50 * 2200 + 20 * 2800, "120 kWh = 90.000 + 110.000 + 56.000 (trước đây 120 × 1.950 = 234.000)");
        line.UnitPrice.Should().Be(decimal.Round(256_000m / 120, 2), "đơn giá hiển thị = bình quân của sản lượng mới");
        invoice.TotalAmount.Should().Be(invoice.Lines.Sum(l => l.Amount));
    }

    [Fact]
    public void EditQuantity_DownAcrossTierBoundary_AlsoSplitsAgain()
    {
        var (invoice, line) = EightyKwhInvoice(Tiers);

        invoice.EditLine(line.Id, 40, null, null, null).IsSuccess.Should().BeTrue();

        line.Amount.Should().Be(40 * 1800, "40 kWh nằm trọn bậc 1");
    }

    [Fact]
    public void EditWithExplicitAmountOrUnitPrice_KeepsWhatTheUserTyped()
    {
        var (invoice, line) = EightyKwhInvoice(Tiers);

        invoice.EditLine(line.Id, 120, null, 300_000, null).IsSuccess.Should().BeTrue();
        line.Amount.Should().Be(300_000, "người dùng nhập thành tiền ⇒ giữ nguyên");

        invoice.ResetLine(line.Id).IsSuccess.Should().BeTrue();
        invoice.EditLine(line.Id, 100, 2_000, null, null).IsSuccess.Should().BeTrue();
        line.UnitPrice.Should().Be(2_000, "người dùng nhập đơn giá ⇒ tính theo đơn giá đó, không chia bậc");
    }

    [Fact]
    public void EditNoteOnly_DoesNotRecomputeAmount()
    {
        var (invoice, line) = EightyKwhInvoice(Tiers);
        var before = line.Amount;

        invoice.EditLine(line.Id, null, null, null, "Ghi chú").IsSuccess.Should().BeTrue();

        line.Amount.Should().Be(before);
    }

    [Fact]
    public void EditQuantity_OnSinglePriceLine_StillQuantityTimesPrice()
    {
        var (invoice, line) = EightyKwhInvoice();

        invoice.EditLine(line.Id, 120, null, null, null).IsSuccess.Should().BeTrue();

        line.Amount.Should().Be(120 * 3800);
    }
}

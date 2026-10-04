using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.UnitTests.Domain;

public sealed class FeeTypeTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1);

    private static FeeType Electricity() => FeeType.DefaultsFor(Guid.NewGuid())[0];

    [Fact]
    public void Defaults_AreMeteredElectricityAndWater_NotAttachedToContracts_WithoutPrice()
    {
        var defaults = FeeType.DefaultsFor(Guid.NewGuid());

        defaults.Select(f => (f.Name, f.Group, f.Unit, f.SystemCode, f.AutoAttach)).Should().Equal(
            ("Điện", FeeGroup.Metered, "kWh", FeeSystemCodes.Electricity, false),
            ("Nước", FeeGroup.Metered, "m³", FeeSystemCodes.Water, false));
        defaults.Should().OnlyContain(f => f.Prices.Count == 0);
    }

    [Fact]
    public void ChargeBasis_IsRequiredOnlyForServiceGroup_AndQuantityOnlyForPerUnit()
    {
        var perOccupant = FeeType.Create(Guid.NewGuid(), "Nước theo người", FeeGroup.Service, ChargeBasis.PerOccupant, "người", true, null, 0);
        perOccupant.ChargeBasis.Should().Be(ChargeBasis.PerOccupant);
        perOccupant.AttachQuantity.Should().Be(1);

        var parking = FeeType.Create(Guid.NewGuid(), "Giữ xe", FeeGroup.Service, ChargeBasis.PerUnit, "xe", false, 2, 0);
        parking.AttachQuantity.Should().Be(2, "2 xe = 2 gói");

        ((Action)(() => FeeType.Create(Guid.NewGuid(), "Rác", FeeGroup.Service, null, "phòng", true, null, 0))).Should().Throw<ArgumentException>();
        ((Action)(() => FeeType.Create(Guid.NewGuid(), "Điện", FeeGroup.Metered, ChargeBasis.PerRoom, "kWh", true, null, 0))).Should().Throw<ArgumentException>();
        ((Action)(() => FeeType.Create(Guid.NewGuid(), "Mạng", FeeGroup.Service, ChargeBasis.PerRoom, "phòng", true, 2, 0))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolvePrice_PicksLatestEffectiveOnOrBeforeDate()
    {
        var fee = Electricity();
        fee.AddPrice(Oct1, 3500, null, null);
        fee.AddPrice(Oct1.AddMonths(2), 3800, null, null);

        fee.ResolvePrice(Oct1.AddDays(-1)).Should().BeNull();
        fee.ResolvePrice(Oct1)!.UnitPrice.Should().Be(3500);
        fee.ResolvePrice(Oct1.AddMonths(2).AddDays(-1))!.UnitPrice.Should().Be(3500);
        fee.ResolvePrice(Oct1.AddMonths(5))!.UnitPrice.Should().Be(3800);
    }

    [Fact]
    public void AddPrice_RejectsSameDate_AndLockedPeriod()
    {
        var fee = Electricity();
        fee.AddPrice(Oct1, 3500, null, null).IsSuccess.Should().BeTrue();

        fee.AddPrice(Oct1, 3600, null, null).Error.Should().Be(FeeErrors.PriceDateExists);
        fee.AddPrice(Oct1.AddDays(10), 3600, null, lockedUntil: Oct1.AddDays(30)).Error.Should().Be(FeeErrors.PriceLocked);

    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(450)]
    public void Electricity_HasOneRate_ForAnyUsage(int kWh)
    {
        var price = Electricity().AddPrice(Oct1, 3500, null, null).Value!;

        price.Amount(kWh).Should().Be(kWh * 3500, "điện trọ tính 1 giá, không bậc thang như hộ gia đình");
        price.Amount(kWh, unitPriceOverride: 3000).Should().Be(kWh * 3000);
    }

    [Fact]
    public void MeteredFee_IsNeverAutoAttached()
    {
        var fee = FeeType.Create(Guid.NewGuid(), "Điện tầng 2", FeeGroup.Metered, null, "kWh", autoAttach: true, null, 0);
        fee.AutoAttach.Should().BeFalse();

        fee.Update("Điện tầng 2", "kWh", autoAttach: true, null, 0);
        fee.AutoAttach.Should().BeFalse("điện / nước theo công tơ đi theo phòng, không gắn vào HĐ");
    }

    [Fact]
    public void Amount_RoundsToWholeDong()
    {
        var water = FeeType.Create(Guid.NewGuid(), "Nước", FeeGroup.Metered, null, "m³", true, null, 1);
        water.AddPrice(Oct1, 15500.5m, null, null).Value!.Amount(3).Should().Be(46502);
    }
}

public sealed class ContractFeeTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly Guid Water = Guid.NewGuid();
    private static readonly Guid Parking = Guid.NewGuid();

    private static Contract ActiveContract()
    {
        var renter = Guid.NewGuid();
        var data = new ContractDraftData(renter, Start, null, null, null, null, 3_000_000, 0, null, 5, ChargeMode.Prepaid,
            ProrationMode.Daily, 5, 30, [PaymentMethod.Cash], 2, null, null, [new OccupantInput(renter, Start, null, null, null)],
            Fees: [new ContractFeeInput(Water, 1, null), new ContractFeeInput(Parking, 1, null)]);
        var contract = Contract.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "HD2026-0001", data);
        contract.Activate(new ActivationContext(Start, DateTimeOffset.UtcNow, true, 4, true, "{}", null, new DateOnly(1990, 1, 1), true))
            .IsSuccess.Should().BeTrue();
        return contract;
    }

    [Fact]
    public void ChangeFee_FromPeriodStart_ClosesOldRegistration_AndKeepsHistory()
    {
        var contract = ActiveContract();

        contract.ChangeFee(new ContractFeeInput(Parking, 2, 90_000), Start.AddMonths(1), null).IsSuccess.Should().BeTrue();

        var parking = contract.Fees.Where(f => f.FeeTypeId == Parking).OrderBy(f => f.EffectiveFrom).ToList();
        parking.Should().HaveCount(2);
        parking[0].EffectiveTo.Should().Be(Start.AddMonths(1).AddDays(-1));
        parking[1].Should().Match<ContractFee>(f => f.Quantity == 2 && f.UnitPriceOverride == 90_000 && f.EffectiveTo == null);
    }

    [Fact]
    public void ChangeFee_Rejects_MidPeriod_BilledPeriod_AndOutOfOrderChanges()
    {
        var contract = ActiveContract();
        var nextPeriod = Start.AddMonths(1);

        contract.ChangeFee(new ContractFeeInput(Water, 1, 20_000), nextPeriod.AddDays(3), null).Error.Should().Be(ContractErrors.NotPeriodStart);
        contract.ChangeFee(new ContractFeeInput(Water, 1, 20_000), nextPeriod, firstOpenPeriodStart: nextPeriod.AddMonths(1))
            .Error.Should().Be(ContractErrors.PeriodAlreadyBilled);

        contract.ChangeFee(new ContractFeeInput(Water, 1, 20_000), nextPeriod.AddMonths(1), null).IsSuccess.Should().BeTrue();
        contract.ChangeFee(new ContractFeeInput(Water, 1, 25_000), nextPeriod, null).Error.Should().Be(ContractErrors.FeeLaterChangeExists);
    }

    [Fact]
    public void RemoveFee_EndsRegistration_OrDeletesItWhenStartingSameDay()
    {
        var contract = ActiveContract();

        contract.RemoveFee(Parking, Start.AddMonths(2), null).IsSuccess.Should().BeTrue();
        contract.Fees.Single(f => f.FeeTypeId == Parking).EffectiveTo.Should().Be(Start.AddMonths(2).AddDays(-1));

        contract.RemoveFee(Water, Start, null).IsSuccess.Should().BeTrue();
        contract.Fees.Should().NotContain(f => f.FeeTypeId == Water);
        contract.RemoveFee(Water, Start.AddMonths(1), null).Error.Should().Be(ContractErrors.FeeNotRegistered);
    }
}

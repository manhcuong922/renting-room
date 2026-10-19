using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Domain.UnitTests;

/// <summary>BL-BR-20: aggregate chỉ báo "dữ liệu tính tiền đổi" khi giá trị thật sự đổi — đổi y nguyên thì nháp không bị đánh dấu thừa.</summary>
public sealed class BillingDataEventTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Representative = Guid.NewGuid();
    private static readonly BillingSchedule Schedule = BillingSchedule.Single(5, ChargeMode.Prepaid);

    private static Contract NewActive()
    {
        var data = new ContractDraftData(
            Representative, Start, Start.AddYears(1).AddDays(-1), SignedDate: null, SignedPlace: null, EffectiveDate: null,
            MonthlyRent: 3_500_000, DepositAmount: 3_500_000, DepositTerms: null, NoticeDays: 30, [PaymentMethod.Cash], CopiesCount: 2,
            TermsText: null, Note: null, [new OccupantInput(Representative, Start, null, null, null)]);
        var contract = Contract.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "hd2026-0001", data);
        contract.Activate(new ActivationContext(Start, Now, true, "{}", "Nội quy")).IsSuccess.Should().BeTrue();
        contract.ClearDomainEvents();
        return contract;
    }

    [Fact]
    public void ChangeRent_RaisesOnlyWhenValuesDiffer()
    {
        var contract = NewActive();
        var nextPeriod = new DateOnly(2026, 11, 5);

        contract.ChangeRent(nextPeriod, 4_000_000, "PL01", null, null, Schedule).IsSuccess.Should().BeTrue();
        contract.DomainEvents.Should().ContainSingle().Which.Should().Be(new ContractBillingChanged(contract.Id));

        contract.ClearDomainEvents();
        contract.ChangeRent(nextPeriod, 4_000_000, "PL01", null, null, Schedule).IsSuccess.Should().BeTrue();
        contract.DomainEvents.Should().BeEmpty("cùng giá, cùng phụ lục ⇒ không có gì đổi");
    }

    [Fact]
    public void NonBillingChanges_DoNotRaise()
    {
        var contract = NewActive();

        contract.UpdateNote("ghi chú").IsSuccess.Should().BeTrue();
        contract.SetSignedDocument(true, "tủ hồ sơ").IsSuccess.Should().BeTrue();
        contract.Extend(Start.AddYears(2)).IsSuccess.Should().BeTrue();

        contract.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Liquidation_ChangesEndDate_AndRaises()
    {
        var contract = NewActive();

        contract.StartLiquidation(Start.AddDays(10), TerminationReason.MutualAgreement, null, null, Start).IsSuccess.Should().BeTrue();

        contract.DomainEvents.Should().Contain(new ContractBillingChanged(contract.Id));
    }

    [Fact]
    public void Meter_RaisesOnInstallRecordAndRealCorrection_NotOnNoOpCorrection()
    {
        var meter = Meter.Install(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "E-101", Start, 100, null);
        meter.DomainEvents.Should().BeEquivalentTo(new object[] { new MeterChanged(meter.Id, meter.RoomId), new MeterReadingChanged(meter.Id) });
        meter.ClearDomainEvents();

        var reading = meter.Record(ReadingKind.Adhoc, Start.AddDays(10), 150, null, null).Value!;
        meter.DomainEvents.Should().ContainSingle().Which.Should().Be(new MeterReadingChanged(meter.Id));
        meter.ClearDomainEvents();

        meter.Correct(reading.Id, 150, null).IsSuccess.Should().BeTrue();
        meter.DomainEvents.Should().BeEmpty("sửa về đúng giá trị đang có ⇒ không đổi");

        meter.Correct(reading.Id, 160, null).IsSuccess.Should().BeTrue();
        meter.DomainEvents.Should().ContainSingle().Which.Should().Be(new MeterReadingChanged(meter.Id));
    }

    [Fact]
    public void FeeType_RaisesCreated_ThenPriceAndInfoChanges()
    {
        var fee = FeeType.Create(Guid.NewGuid(), "Mạng", FeeGroup.Service, ChargeBasis.PerRoom, "phòng", true, null, 0);
        fee.DomainEvents.Should().Contain(new FeeTypeCreated(fee.Id));
        fee.ClearDomainEvents();

        fee.AddPrice(Start, 100_000, null, null).IsSuccess.Should().BeTrue();
        fee.DomainEvents.Should().ContainSingle().Which.Should().Be(new FeePriceChanged(fee.Id, Start));
        fee.ClearDomainEvents();

        fee.Update("Mạng", "phòng", true, null, 0);
        fee.DomainEvents.Should().BeEmpty("sửa y nguyên ⇒ không đổi");

        fee.Update("Internet", "phòng", true, null, 0);
        fee.DomainEvents.Should().ContainSingle().Which.Should().Be(new FeeTypeChanged(fee.Id));
    }
}

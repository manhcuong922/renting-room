using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;

namespace renting_room.Application.UnitTests;

/// <summary>BL-BR-20: sự kiện của một lần lưu → phạm vi nháp cần đánh dấu "Cần tính lại".</summary>
public sealed class DraftStaleScopeTests
{
    private static readonly Guid Organization = Guid.NewGuid();

    private static DraftStaleScope? Scope(params IDomainEvent[] events) => DraftStaleScope.From(new DomainEventBatch(Organization, events));

    [Fact]
    public void ContractRoomAndMeterEvents_AreCollected()
    {
        var (contract, meter, room) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var scope = Scope(new ContractBillingChanged(contract), new MeterReadingChanged(meter), new MeterChanged(meter, room))!;

        scope.ContractIds.Should().Equal(contract);
        scope.MeterIds.Should().Equal(meter);
        scope.RoomIds.Should().Equal(room);
        scope.FeeTypeIds.Should().BeEmpty();
    }

    [Fact]
    public void PriceChanges_UseEarliestEffectiveDate_InfoChangeAffectsEveryPeriod()
    {
        var fee = Guid.NewGuid();

        Scope(new FeePriceChanged(fee, new DateOnly(2026, 12, 1)), new FeePriceChanged(fee, new DateOnly(2026, 11, 1)))!
            .PriceFrom.Should().Be(new DateOnly(2026, 11, 1));
        Scope(new FeePriceChanged(fee, new DateOnly(2026, 12, 1)), new FeeTypeChanged(fee))!
            .PriceFrom.Should().Be(DateOnly.MinValue);
    }

    [Fact]
    public void NewFeeType_WithInitialPrice_AffectsNoDraft()
    {
        var fee = Guid.NewGuid();

        Scope(new FeeTypeCreated(fee), new FeeTypeChanged(fee), new FeePriceChanged(fee, new DateOnly(2026, 11, 1))).Should().BeNull();
    }

    [Fact]
    public void NoOrganization_OrNoRelevantEvent_MarksNothing()
    {
        DraftStaleScope.From(new DomainEventBatch(Guid.Empty, [new ContractBillingChanged(Guid.NewGuid())])).Should().BeNull();
        Scope().Should().BeNull();
    }
}

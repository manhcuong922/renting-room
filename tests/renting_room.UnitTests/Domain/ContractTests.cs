using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.UnitTests.Domain;

public sealed class ContractTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Representative = Guid.NewGuid();
    private static readonly Guid OccupantA = Guid.NewGuid();
    private static readonly Guid OccupantB = Guid.NewGuid();

    private static ContractDraftData Draft(params Guid[] occupants) => new(
        Representative, Start, Start.AddYears(1).AddDays(-1), SignedDate: null, SignedPlace: null, EffectiveDate: null,
        MonthlyRent: 3_500_000, DepositAmount: 3_500_000, DepositTerms: null, BillingAnchorDay: 5, ChargeMode.Prepaid,
        ProrationMode.Daily, PaymentDueDays: 5, NoticeDays: 30, [PaymentMethod.Cash], CopiesCount: 2, TermsText: null, Note: null,
        occupants.Select(id => new OccupantInput(id, Start, null, null, null)).ToList());

    private static ActivationContext Context(
        bool roomAvailable = true, int maxOccupants = 2, bool lessorComplete = true, DateOnly? representativeDob = null,
        bool hasPhone = true, DateOnly? today = null) =>
        new(today ?? Start, Now, roomAvailable, maxOccupants, lessorComplete, "{}", "Nội quy",
            representativeDob ?? new DateOnly(2000, 1, 1), hasPhone);

    private static Contract NewDraft(params Guid[] occupants) =>
        Contract.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "hd2026-0001", Draft(occupants.Length == 0 ? [OccupantA] : occupants));

    private static Contract NewActive()
    {
        var contract = NewDraft();
        contract.Activate(Context()).IsSuccess.Should().BeTrue();
        return contract;
    }

    [Fact]
    public void CreateDraft_NormalizesNumber_AndCreatesInitialRentTerm()
    {
        var contract = NewDraft();

        contract.ContractNo.Should().Be("HD2026-0001");
        contract.Status.Should().Be(ContractStatus.Draft);
        contract.RentTerms.Should().ContainSingle(t => t.EffectiveFrom == Start && t.MonthlyRent == 3_500_000);
        contract.CurrentRent(Start).Should().Be(3_500_000);
    }

    [Fact]
    public void Activate_SetsSignedAndEffectiveDate_AndSnapshot()
    {
        var contract = NewDraft();

        contract.Activate(Context()).IsSuccess.Should().BeTrue();

        contract.Status.Should().Be(ContractStatus.Active);
        contract.SignedDate.Should().Be(Start);
        contract.EffectiveDate.Should().Be(Start);
        contract.SigningSnapshot.Should().Be("{}");
        contract.HouseRulesSnapshot.Should().Be("Nội quy");
    }

    [Theory]
    [MemberData(nameof(ActivationFailures))]
    public void Activate_Fails_WhenPreconditionNotMet(ActivationContext context, string expectedCode)
    {
        NewDraft().Activate(context).Error!.Code.Should().Be(expectedCode);
    }

    public static TheoryData<ActivationContext, string> ActivationFailures => new()
    {
        { Context(roomAvailable: false), "ROOM_UNAVAILABLE" },
        { Context(lessorComplete: false), "LESSOR_INFO_INCOMPLETE" },
        { Context(hasPhone: false), "REPRESENTATIVE_PHONE_REQUIRED" },
        { Context(representativeDob: new DateOnly(2010, 1, 1)), "REPRESENTATIVE_UNDERAGE" },   // BLDS Điều 117
        { Context(today: Start.AddDays(-5)), "START_DATE_IN_FUTURE" }
    };

    [Fact]
    public void Activate_Fails_WhenOccupantsExceedRoomCapacity()
    {
        var contract = NewDraft(OccupantA, OccupantB);

        contract.Activate(Context(maxOccupants: 1)).Error.Should().Be(ContractErrors.RoomCapacityExceeded);
    }

    [Fact]
    public void Activate_Fails_WithoutOccupant()
    {
        var contract = Contract.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "X", Draft([]));

        contract.Activate(Context()).Error.Should().Be(ContractErrors.NoOccupant);
    }

    [Fact]
    public void Representative_JustTurned18_OnSignedDate_IsAllowed()
    {
        var contract = NewDraft();

        contract.Activate(Context(representativeDob: Start.AddYears(-18))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AddOccupant_RespectsCapacity_UnlessOverridden()
    {
        var contract = NewActive();
        var input = new OccupantInput(OccupantB, Start.AddDays(10), null, "Bạn", null);

        contract.AddOccupant(input, roomMaxOccupants: 1, overrideCapacity: false).Error.Should().Be(ContractErrors.RoomCapacityExceeded);
        contract.AddOccupant(input, roomMaxOccupants: 1, overrideCapacity: true).IsSuccess.Should().BeTrue();
        contract.AddOccupant(input, roomMaxOccupants: 5, overrideCapacity: false).Error.Should().Be(ContractErrors.OccupantOverlap);
    }

    [Fact]
    public void ChangeRent_OnlyFromPeriodStart_AndNotBeforeFirstOpenPeriod()
    {
        var contract = NewActive();

        contract.ChangeRent(new DateOnly(2026, 11, 10), 4_000_000, "PL01", null, null).Error.Should().Be(ContractErrors.NotPeriodStart);
        contract.ChangeRent(new DateOnly(2026, 11, 5), 4_000_000, "PL01", null, new DateOnly(2026, 12, 5))
            .Error.Should().Be(ContractErrors.PeriodAlreadyBilled);
        contract.ChangeRent(new DateOnly(2026, 12, 5), 4_000_000, "PL01", null, null).IsSuccess.Should().BeTrue();

        contract.CurrentRent(new DateOnly(2026, 12, 4)).Should().Be(3_500_000);
        contract.CurrentRent(new DateOnly(2026, 12, 5)).Should().Be(4_000_000);
    }

    [Fact]
    public void GiveNotice_WarnsWhenShorterThanNoticeDays()
    {
        var contract = NewActive();

        var notice = contract.GiveNotice(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 20));

        notice.Value!.ShorterThanNoticePeriod.Should().BeTrue();
        notice.Value.ActualDays.Should().Be(19);
    }

    [Fact]
    public void LessorUnilateralTermination_RequiresArticle172Ground()
    {
        var contract = NewActive();
        var today = Start.AddMonths(3);

        contract.StartLiquidation(today, TerminationReason.LessorUnilateral, null, null, today)
            .Error.Should().Be(ContractErrors.TerminationGroundRequired);
        contract.StartLiquidation(today, TerminationReason.LessorUnilateral, TerminationGround.RentArrears3Months, null, today)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Liquidation_CompleteClosesOccupantsAndVehicles_CancelRestoresActive()
    {
        var contract = NewActive();
        contract.RegisterVehicle(new VehicleInput(null, VehicleType.Motorbike, "29-B1 123.45", "Đỏ", Start, null)).IsSuccess.Should().BeTrue();
        var end = Start.AddMonths(2);

        contract.StartLiquidation(end, TerminationReason.MutualAgreement, null, null, end).IsSuccess.Should().BeTrue();
        contract.CancelLiquidation().IsSuccess.Should().BeTrue();
        contract.Status.Should().Be(ContractStatus.Active);
        contract.ActualEndDate.Should().BeNull();

        contract.StartLiquidation(end, TerminationReason.MutualAgreement, null, null, end);
        contract.CompleteLiquidation(Now).Error.Should().Be(ContractErrors.LiquidationBeforeEndDate);
        contract.CompleteLiquidation(new DateTimeOffset(end.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(7))).IsSuccess.Should().BeTrue();

        contract.Status.Should().Be(ContractStatus.Ended);
        contract.Occupants.Should().OnlyContain(o => o.MoveOutDate == end);
        contract.Vehicles.Should().OnlyContain(v => v.RegisteredTo == end && v.PlateNumber == "29B112345");
    }

    [Fact]
    public void Assets_EditableOnlyInDraft_ReturnRecordedOnlyWhenLiquidating()
    {
        var contract = NewDraft();
        var asset = contract.AddAsset(new AssetInput("Điều hòa", 1, "Mới 90%", 5_000_000, null)).Value!;
        contract.Activate(Context());

        contract.AddAsset(new AssetInput("Giường", 1, null, null, null)).Error.Should().Be(ContractErrors.NotDraft);
        contract.RecordAssetReturn(asset.Id, "Hỏng remote", 200_000).Error.Should().Be(ContractErrors.NotLiquidating);

        contract.StartLiquidation(Start.AddMonths(1), TerminationReason.Expired, null, null, Start.AddMonths(1));
        contract.RecordAssetReturn(asset.Id, "Hỏng remote", 200_000).IsSuccess.Should().BeTrue();
        contract.Assets.Single().CompensationValue.Should().Be(200_000);
    }

    [Fact]
    public void Cancel_OnlyDraft()
    {
        NewDraft().Cancel("Khách không đến", Now).IsSuccess.Should().BeTrue();
        NewActive().Cancel("x", Now).Error.Should().Be(ContractErrors.NotDraft);
    }

    [Fact]
    public void CancelDraft_ReleasesRegisteredVehicles()
    {
        var contract = NewDraft();
        contract.RegisterVehicle(new VehicleInput(null, VehicleType.Motorbike, "29B112345", null, Start, null));

        contract.Cancel("Khách không đến", Now).IsSuccess.Should().BeTrue();

        contract.Vehicles.Should().OnlyContain(v => !v.IsActive);
    }

    [Fact]
    public void Activate_ImportedContract_DefaultSignedDateNotAfterAgreedEffectiveDate()
    {
        // HĐ nhập lại: bắt đầu 01/09, thỏa thuận hiệu lực từ 25/08 (đặt cọc trước), không ghi ngày ký.
        var data = Draft(OccupantA) with { StartDate = new DateOnly(2026, 9, 1), EndDate = null, EffectiveDate = new DateOnly(2026, 8, 25) };
        var contract = Contract.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "X", data with
        {
            Occupants = [new OccupantInput(OccupantA, new DateOnly(2026, 9, 1), null, null, null)]
        });

        contract.Activate(Context(today: new DateOnly(2026, 10, 2))).IsSuccess.Should().BeTrue();

        contract.SignedDate.Should().Be(new DateOnly(2026, 8, 25));
        contract.EffectiveDate.Should().Be(new DateOnly(2026, 8, 25));
    }

    [Fact]
    public void ChangeRent_IsNotAllowedOnDraft()
    {
        NewDraft().ChangeRent(new DateOnly(2026, 11, 5), 4_000_000, null, null, null).Error.Should().Be(ContractErrors.NotActive);
    }
}

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

        contract.StartLiquidation(Start.AddMonths(1), TerminationReason.MutualAgreement, null, null, Start.AddMonths(1));
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

    private static Contract NewActiveIndefinite()
    {
        var contract = Contract.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "hd2026-0002", Draft(OccupantA) with { EndDate = null });
        contract.Activate(Context()).IsSuccess.Should().BeTrue();
        return contract;
    }

    private static IEnumerable<string> WarningCodes(Contract contract) => ContractWarnings.For(contract).Select(w => w.Code);

    [Fact]
    public void ExpiredReason_OnlyForFixedTermContract_EndingOnOrAfterEndDate()
    {
        var end = Start.AddYears(1).AddDays(-1);
        NewActive().StartLiquidation(end.AddDays(-10), TerminationReason.Expired, null, null, end.AddDays(-10))
            .Error.Should().Be(ContractErrors.ExpiredReasonInvalid, "trả sớm hơn hạn không phải hết hạn");
        NewActiveIndefinite().StartLiquidation(Start.AddMonths(2), TerminationReason.Expired, null, null, Start.AddMonths(2))
            .Error.Should().Be(ContractErrors.ExpiredReasonInvalid, "HĐ không thời hạn không bao giờ hết hạn");
        NewActive().StartLiquidation(end, TerminationReason.Expired, null, null, end).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void IndefiniteContract_LessorEndsWithNinetyDayNotice_CannotExtend()
    {
        NewActiveIndefinite().Extend(Start.AddYears(1)).Error.Should().Be(ContractErrors.CannotExtendIndefinite);
        NewActive().StartLiquidation(Start.AddMonths(1), TerminationReason.LessorUnilateral, TerminationGround.IndefiniteTermNotice, null, Start)
            .Error.Should().Be(ContractErrors.IndefiniteGroundOnly);

        var shortNotice = NewActiveIndefinite();
        shortNotice.StartLiquidation(Start.AddDays(45), TerminationReason.LessorUnilateral, TerminationGround.IndefiniteTermNotice, null, Start)
            .IsSuccess.Should().BeTrue();
        WarningCodes(shortNotice).Should().Contain("LESSOR_TERMINATION_SHORT_NOTICE");

        var enough = NewActiveIndefinite();
        enough.StartLiquidation(Start.AddDays(90), TerminationReason.LessorUnilateral, TerminationGround.IndefiniteTermNotice, null, Start)
            .IsSuccess.Should().BeTrue("được hẹn trước 90 ngày dù mức thường là 60");
        WarningCodes(enough).Should().NotContain("LESSOR_TERMINATION_SHORT_NOTICE");
        NewActiveIndefinite().StartLiquidation(Start.AddDays(91), TerminationReason.LessorUnilateral, TerminationGround.IndefiniteTermNotice, null, Start)
            .Error.Should().Be(ContractErrors.InvalidEndDate);
    }

    [Fact]
    public void Abandoned_AllowsPastMoveOutDate_RequiresNote_AndWarns()
    {
        var today = Start.AddMonths(3);
        NewActive().StartLiquidation(today.AddDays(-12), TerminationReason.Abandoned, null, null, today)
            .Error.Should().Be(ContractErrors.AbandonedNoteRequired);

        var contract = NewActive();
        contract.StartLiquidation(today.AddDays(-12), TerminationReason.Abandoned, null,
            "Phát hiện phòng bỏ trống, còn 1 vali; tổ trưởng chứng kiến", today).IsSuccess.Should().BeTrue();

        contract.ActualEndDate.Should().Be(today.AddDays(-12));
        WarningCodes(contract).Should().Contain("LESSEE_ABANDONED");
    }

    [Fact]
    public void LesseeLeavingWithoutEnoughNotice_IsWarned()
    {
        var noNotice = NewActive();
        noNotice.StartLiquidation(Start.AddMonths(2), TerminationReason.LesseeUnilateral, null, null, Start.AddMonths(2));
        WarningCodes(noNotice).Should().Contain("LESSEE_TERMINATION_SHORT_NOTICE");

        var noticed = NewActive();
        noticed.GiveNotice(Start.AddMonths(1), Start.AddMonths(2));
        noticed.StartLiquidation(Start.AddMonths(2), TerminationReason.LesseeUnilateral, null, null, Start.AddMonths(2));
        WarningCodes(noticed).Should().NotContain("LESSEE_TERMINATION_SHORT_NOTICE");
    }

    // ------------------------------------------------------------------ CT-BR-44/45: cờ cần chủ trọ xử lý

    private static Contract NewActiveWith(params Guid[] occupants)
    {
        var contract = NewDraft(occupants);
        contract.Activate(Context(maxOccupants: 4)).IsSuccess.Should().BeTrue();
        return contract;
    }

    private static void MoveOut(Contract contract, Guid renterId, DateOnly date) =>
        contract.EndOccupancy(contract.Occupants.Single(o => o.RenterId == renterId).Id, date).IsSuccess.Should().BeTrue();

    [Fact]
    public void RepresentativeMovesOut_WhileOthersStay_IsFlagged_RoomStillHasContract()
    {
        var contract = NewActiveWith(Representative, OccupantA);
        var today = Start.AddMonths(2);
        MoveOut(contract, Representative, today.AddDays(-1));

        contract.Flags(today).Should().Equal(ContractFlag.RepresentativeMovedOut);
        contract.Status.Should().Be(ContractStatus.Active);
        ContractWarnings.For(contract, today: today).Select(w => w.Code).Should().Contain("REPRESENTATIVE_MOVED_OUT");

        MoveOut(contract, OccupantA, today);
        contract.Flags(today).Should().Equal([ContractFlag.NoOccupantLeft], "người cuối cùng chuyển đi ⇒ nhắc thanh lý, không tự về phòng trống");
    }

    [Fact]
    public void RepresentativeWhoNeverLivedThere_IsNotFlagged()
    {
        var contract = NewActiveWith(OccupantA, OccupantB); // bố ký cho con, không ở
        contract.Flags(Start.AddMonths(1)).Should().BeEmpty();
    }

    [Fact]
    public void Expired_AwaitsDecision_HoldoverThenExtend()
    {
        var contract = NewActive();
        var end = Start.AddYears(1).AddDays(-1);

        contract.StartHoldover(end, null).Error.Should().Be(ContractErrors.NotExpired);
        contract.Flags(end.AddDays(1)).Should().Equal(ContractFlag.ExpiredAwaitingDecision);

        contract.StartHoldover(end.AddDays(3), "Chưa kịp ký lại").IsSuccess.Should().BeTrue();
        contract.Flags(end.AddDays(10)).Should().Equal(ContractFlag.Holdover);
        contract.StartHoldover(end.AddDays(4), null).Error.Should().Be(ContractErrors.HoldoverAlready);

        contract.Extend(end.AddYears(1)).IsSuccess.Should().BeTrue();
        contract.HoldoverSince.Should().BeNull();
        contract.Flags(end.AddDays(10)).Should().BeEmpty();
    }

    [Fact]
    public void ResignDraft_CopiesTermsAndRemainingOccupants()
    {
        var contract = NewActiveWith(Representative, OccupantA, OccupantB);
        var handover = Start.AddMonths(3);
        MoveOut(contract, Representative, handover);

        contract.ResignDraft(handover, Representative, null).Error.Should().Be(ContractErrors.ResignRepresentativeNotOccupant);
        var data = contract.ResignDraft(handover, OccupantA, null).Value!;

        data.StartDate.Should().Be(handover.AddDays(1));
        data.RepresentativeRenterId.Should().Be(OccupantA);
        data.Occupants.Select(o => o.RenterId).Should().BeEquivalentTo([OccupantA, OccupantB]);
        data.Occupants.Should().OnlyContain(o => o.MoveInDate == handover.AddDays(1) && o.RelationshipType == null,
            "chủ hộ cũ (người ký) đã đi ⇒ khai lại quan hệ với người đứng tên mới");
        data.MonthlyRent.Should().Be(3_500_000);
        data.DepositAmount.Should().Be(3_500_000);
        data.BillingAnchorDay.Should().Be(contract.BillingAnchorDay);

        MoveOut(contract, OccupantA, handover);
        MoveOut(contract, OccupantB, handover);
        contract.ResignDraft(handover, OccupantA, null).Error.Should().Be(ContractErrors.ResignNoOccupantLeft);
    }
}

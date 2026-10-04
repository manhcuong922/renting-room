using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Domain.Contracts;

/// <summary>
/// Hợp đồng thuê 1 phòng. Vòng đời: Draft → Active → Liquidating → Ended (hoặc Draft → Cancelled).
/// Giá thuê, người ở, tài sản, xe là entity con — mọi thay đổi đi qua method của aggregate để giữ bất biến.
/// </summary>
public sealed class Contract : TenantEntity
{
    public const int MaxStartDaysAhead = 1;
    public const int MaxLiquidationDaysAhead = 60;

    private readonly List<ContractRentTerm> _rentTerms = [];
    private readonly List<ContractOccupant> _occupants = [];
    private readonly List<ContractAsset> _assets = [];
    private readonly List<ContractVehicle> _vehicles = [];
    private readonly List<ContractFee> _fees = [];

    private Contract() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public string ContractNo { get; private set; } = null!;
    public ContractStatus Status { get; private set; }
    public Guid RepresentativeRenterId { get; private set; }
    public DateOnly? SignedDate { get; private set; }
    public string? SignedPlace { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateOnly? ActualEndDate { get; private set; }
    public DateOnly? NoticeGivenDate { get; private set; }
    public DateOnly? PlannedMoveOutDate { get; private set; }
    public int NoticeDays { get; private set; }
    public decimal DepositAmount { get; private set; }
    public string? DepositTerms { get; private set; }
    public int BillingAnchorDay { get; private set; }
    public ChargeMode ChargeMode { get; private set; }
    public ProrationMode ProrationMode { get; private set; }
    public int PaymentDueDays { get; private set; }
    public PaymentMethod[] PaymentMethods { get; private set; } = [];
    public int CopiesCount { get; private set; }
    public string? TermsText { get; private set; }
    public string? Note { get; private set; }
    public Guid? PreviousContractId { get; private set; }

    // CT-BR-25: văn bản hợp đồng theo mẫu. Chỉ sửa khi nháp ⇒ đã kích hoạt là bất biến.
    public Guid? TemplateId { get; private set; }
    public ContractType ContractType { get; private set; }
    public string? Title { get; private set; }
    public string? Clauses { get; private set; }
    public string? CustomFieldDefinitions { get; private set; }
    public string? CustomFieldValues { get; private set; }

    /// <summary>
    /// CT-BR-36: chủ hộ khi đăng ký tạm trú chung — phải là người ở. NULL ⇒ người đứng tên là chủ hộ.
    /// Quan hệ của người ở (CT-BR-28) khai so với <see cref="ReferenceRenterId"/>.
    /// </summary>
    public Guid? HouseholdHeadRenterId { get; private set; }

    public Guid ReferenceRenterId => HouseholdHeadRenterId ?? RepresentativeRenterId;

    /// <summary>
    /// CT-BR-19 / LEG-01: bản chụp BẤT BIẾN lúc ký — bên cho thuê, bên thuê (người đứng tên), phòng, ngân hàng.
    /// Sau này sửa hồ sơ người thuê / khu / phòng thì hợp đồng đã ký vẫn giữ đúng thông tin lúc ký.
    /// </summary>
    public string? SigningSnapshot { get; private set; }
    public string? HouseRulesSnapshot { get; private set; }
    public string? UtilityPriceSnapshot { get; private set; }

    public TerminationReason? TerminationReason { get; private set; }

    /// <summary>Ngày ghi nhận bắt đầu thanh lý — đối chiếu thời hạn báo trước khi bên cho thuê đơn phương chấm dứt (CT-BR-21).</summary>
    public DateOnly? LiquidationStartedOn { get; private set; }

    /// <summary>CT-BR-45: ngày chủ trọ chọn "ở tiếp, chưa ký lại" sau khi HĐ hết hạn; gia hạn thì xóa.</summary>
    public DateOnly? HoldoverSince { get; private set; }
    public string? HoldoverNote { get; private set; }
    public TerminationGround? TerminationGround { get; private set; }
    public string? TerminationNote { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyList<ContractRentTerm> RentTerms => _rentTerms;
    public IReadOnlyList<ContractOccupant> Occupants => _occupants;
    public IReadOnlyList<ContractAsset> Assets => _assets;
    public IReadOnlyList<ContractVehicle> Vehicles => _vehicles;
    public IReadOnlyList<ContractFee> Fees => _fees;

    public bool IsOverdue(DateOnly today) => Status == ContractStatus.Active && EndDate < today;

    /// <summary>
    /// CT-BR-44/45: việc chủ trọ cần xử lý trên HĐ đang hiệu lực, tính theo ngày (không lưu). Người còn ở = chưa ghi chuyển đi
    /// hoặc chuyển đi sau hôm nay; người đứng tên không phải người ở (VD bố ký cho con) thì không tính là "rời đi".
    /// </summary>
    public IReadOnlyList<ContractFlag> Flags(DateOnly today)
    {
        if (Status != ContractStatus.Active || StartDate > today)
            return [];

        static bool Stays(ContractOccupant o, DateOnly day) => o.MoveOutDate is null || o.MoveOutDate > day;
        var flags = new List<ContractFlag>();
        var representative = _occupants.Where(o => o.RenterId == RepresentativeRenterId).ToList();
        if (!_occupants.Any(o => Stays(o, today)))
            flags.Add(ContractFlag.NoOccupantLeft);
        else if (representative.Count > 0 && !representative.Any(o => Stays(o, today)))
            flags.Add(ContractFlag.RepresentativeMovedOut);

        if (EndDate is { } end && end < today)
            flags.Add(HoldoverSince is null ? ContractFlag.ExpiredAwaitingDecision : ContractFlag.Holdover);
        return flags;
    }

    public decimal? CurrentRent(DateOnly date) =>
        _rentTerms.Where(t => t.EffectiveFrom <= date).MaxBy(t => t.EffectiveFrom)?.MonthlyRent;

    public IEnumerable<ContractOccupant> OccupantsOn(DateOnly date) => _occupants.Where(o => o.IsStayingOn(date));

    public IReadOnlyList<BillingPeriod> BillingPeriods(DateOnly until) =>
        BillingPeriodCalculator.Periods(StartDate, ActualEndDate, BillingAnchorDay, until);

    // ------------------------------------------------------------------ Nháp

    public static Contract CreateDraft(Guid propertyId, Guid roomId, string contractNo, ContractDraftData data, Guid? previousContractId = null)
    {
        if (propertyId == Guid.Empty || roomId == Guid.Empty)
            throw new ArgumentException("Property and room are required.");
        if (string.IsNullOrWhiteSpace(contractNo))
            throw new ArgumentException("Contract number is required.", nameof(contractNo));

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            RoomId = roomId,
            ContractNo = TextNormalizer.NormalizeCode(contractNo),
            Status = ContractStatus.Draft,
            PreviousContractId = previousContractId
        };
        contract.ApplyDraft(data);
        return contract;
    }

    public Result UpdateDraft(ContractDraftData data)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);

        var members = data.Occupants.Select(o => o.RenterId).Append(data.RepresentativeRenterId).ToHashSet();
        var orphan = _vehicles.FirstOrDefault(v => v.IsActive && v.RenterId is { } owner && !members.Contains(owner));
        if (orphan is not null)
            return Result.Failure(Error.BusinessRule(ContractErrors.VehicleOwnerNotInContract.Code,
                $"Xe {orphan.PlateNumber ?? orphan.VehicleType.ToString()} đăng ký cho người không còn thuộc hợp đồng — " +
                "kết thúc đăng ký xe đó trước, rồi đăng ký lại cho người đang ở."));

        ApplyDraft(data);
        return Result.Success();
    }

    /// <summary>Ghi chú nội bộ của chủ trọ (không phải nội dung đã ký) — sửa được ở mọi trạng thái trừ đã hủy.</summary>
    public Result UpdateNote(string? note)
    {
        if (Status == ContractStatus.Cancelled)
            return Result.Failure(ContractErrors.NotEditable);

        Note = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);

        // Nháp bị hủy phải nhả biển số đã đăng ký giữ, nếu không biển số bị khóa vĩnh viễn trong tổ chức.
        foreach (var vehicle in _vehicles.Where(v => v.IsActive))
            vehicle.End(vehicle.RegisteredFrom);

        Status = ContractStatus.Cancelled;
        CancelReason = reason.Trim();
        CancelledAt = now;
        return Result.Success();
    }

    private void ApplyDraft(ContractDraftData data)
    {
        if (data.EndDate is { } end && end <= data.StartDate)
            throw new ArgumentException("End date must be after start date.");
        if (data.MonthlyRent <= 0 || data.DepositAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(data), "Rent must be positive and deposit non-negative.");
        if (data.Occupants.Select(o => o.RenterId).Distinct().Count() != data.Occupants.Count)
            throw new ArgumentException("Occupants must be distinct.");
        if (data.Occupants.Any(o => o.MoveInDate < data.StartDate))
            throw new ArgumentException("Occupants cannot move in before the contract start date.");
        if (data.PaymentMethods.Count == 0)
            throw new ArgumentException("At least one payment method is required.");

        RepresentativeRenterId = data.RepresentativeRenterId;
        StartDate = data.StartDate;
        EndDate = data.EndDate;
        SignedDate = data.SignedDate;
        SignedPlace = TextNormalizer.TrimToNull(data.SignedPlace);
        EffectiveDate = data.EffectiveDate;
        DepositAmount = data.DepositAmount;
        DepositTerms = TextNormalizer.TrimToNull(data.DepositTerms);
        BillingAnchorDay = data.BillingAnchorDay;
        ChargeMode = data.ChargeMode;
        ProrationMode = data.ProrationMode;
        PaymentDueDays = data.PaymentDueDays;
        NoticeDays = data.NoticeDays;
        PaymentMethods = data.PaymentMethods.Distinct().ToArray();
        CopiesCount = data.CopiesCount;
        TermsText = TextNormalizer.TrimToNull(data.TermsText);
        Note = TextNormalizer.TrimToNull(data.Note);

        if (data.HouseholdHeadRenterId is { } head && data.Occupants.All(o => o.RenterId != head))
            throw new ArgumentException("Household head must be one of the occupants.");
        HouseholdHeadRenterId = data.HouseholdHeadRenterId;

        var document = data.Document ?? ContractDocument.Default();
        TemplateId = document.TemplateId;
        ContractType = document.ContractType;
        Title = document.Title.Trim();
        Clauses = document.ClausesJson;
        CustomFieldDefinitions = document.FieldDefinitionsJson;
        CustomFieldValues = document.FieldValuesJson;

        _rentTerms.Clear();
        _rentTerms.Add(new ContractRentTerm(Id, StartDate, data.MonthlyRent, addendumNo: null, note: null));

        // Khoản thu của nháp: thay toàn bộ, hiệu lực từ ngày bắt đầu (Application đã kiểm thuộc khu / chưa ngừng dùng).
        var fees = data.Fees ?? [];
        if (fees.Select(f => f.FeeTypeId).Distinct().Count() != fees.Count)
            throw new ArgumentException("Fee types must be distinct.");
        _fees.Clear();
        _fees.AddRange(fees.Select(f => new ContractFee(Id, PropertyId, f, StartDate)));

        // Dời ngày bắt đầu nháp về sau ⇒ xe đã đăng ký không được giữ trước ngày bắt đầu HĐ (phí giữ xe M07 tính theo ngày này).
        foreach (var vehicle in _vehicles.Where(v => v.IsActive && v.RegisteredFrom < StartDate))
            vehicle.MoveStart(StartDate);

        _occupants.Clear();
        _occupants.AddRange(data.Occupants.Select(o => new ContractOccupant(Id, o)));
    }

    // ------------------------------------------------------------------ Kích hoạt (bàn giao phòng)

    public Result Activate(ActivationContext context)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);
        if (!context.RoomAvailable)
            return Result.Failure(ContractErrors.RoomUnavailable);
        if (StartDate > context.Today.AddDays(MaxStartDaysAhead))
            return Result.Failure(ContractErrors.StartDateTooFarInFuture);
        if (!context.LessorComplete)
            return Result.Failure(ContractErrors.LessorInfoIncomplete);
        if (_occupants.Count == 0)
            return Result.Failure(ContractErrors.NoOccupant);
        if (!context.OverrideCapacity && ExceedsCapacity(context.RoomMaxOccupants))
            return Result.Failure(ContractErrors.RoomCapacityExceeded);
        if (!context.RepresentativeHasPhone)
            return Result.Failure(ContractErrors.RepresentativePhoneRequired);

        // Không ghi ngày ký: HĐ nhập lại (bắt đầu trong quá khứ) coi như ký ngày bắt đầu; HĐ mới coi như ký hôm nay.
        // Không được muộn hơn ngày hiệu lực đã thỏa thuận (CT-BR-20: hiệu lực không trước ngày ký).
        var defaultSignedDate = StartDate < context.Today ? StartDate : context.Today;
        var signedDate = SignedDate
            ?? (EffectiveDate is { } agreedEffective && agreedEffective < defaultSignedDate ? agreedEffective : defaultSignedDate);
        if (context.RepresentativeDateOfBirth.AgeOn(signedDate) < LessorDetails.MinimumAge)
            return Result.Failure(ContractErrors.RepresentativeUnderage);

        SignedDate = signedDate;
        EffectiveDate ??= signedDate;
        SigningSnapshot = context.SigningSnapshotJson;
        HouseRulesSnapshot = context.HouseRulesSnapshot;
        UtilityPriceSnapshot = context.UtilityPriceSnapshotJson;
        Status = ContractStatus.Active;
        ActivatedAt = context.Now;
        return Result.Success();
    }

    // ------------------------------------------------------------------ Người ở

    public Result AddOccupant(OccupantInput input, int roomMaxOccupants, bool overrideCapacity)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Active))
            return Result.Failure(ContractErrors.NotEditable);
        if (input.MoveInDate < StartDate)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (EndDate is { } end && input.MoveInDate > end)
            return Result.Failure(Status == ContractStatus.Active ? ContractErrors.ExpiredExtendFirst : ContractErrors.DateOutsideContract);
        if (_occupants.Any(o => o.RenterId == input.RenterId && o.OverlapsFrom(input.MoveInDate)))
            return Result.Failure(ContractErrors.OccupantOverlap);
        if (!overrideCapacity && _occupants.Count(o => o.OverlapsFrom(input.MoveInDate)) + 1 > roomMaxOccupants)
            return Result.Failure(ContractErrors.RoomCapacityExceeded);

        _occupants.Add(new ContractOccupant(Id, input));
        return Result.Success();
    }

    public Result EndOccupancy(Guid occupantId, DateOnly moveOutDate)
    {
        if (Status is not (ContractStatus.Active or ContractStatus.Liquidating))
            return Result.Failure(ContractErrors.NotActive);

        var occupant = _occupants.FirstOrDefault(o => o.Id == occupantId);
        if (occupant is null)
            return Result.Failure(ContractErrors.OccupantNotFound);
        if (occupant.MoveOutDate is not null)
            return Result.Failure(ContractErrors.OccupantAlreadyMovedOut);
        if (moveOutDate < occupant.MoveInDate || (ActualEndDate is { } actualEnd && moveOutDate > actualEnd))
            return Result.Failure(ContractErrors.DateOutsideContract);

        occupant.MoveOut(moveOutDate);
        return Result.Success();
    }

    // ------------------------------------------------------------------ Phụ lục: giá, gia hạn, báo trả phòng

    /// <summary>CT-BR-05: giá mới chỉ áp từ đầu một kỳ và từ kỳ chưa lập phiếu.</summary>
    public Result ChangeRent(DateOnly effectiveFrom, decimal monthlyRent, string? addendumNo, string? note, DateOnly? firstOpenPeriodStart)
    {
        // Nháp sửa giá trực tiếp bằng UpdateDraft; phụ lục chỉ có ý nghĩa với hợp đồng đã ký.
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (effectiveFrom <= StartDate || (EndDate is { } end && effectiveFrom > end))
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (!BillingPeriodCalculator.IsPeriodStart(StartDate, BillingAnchorDay, effectiveFrom))
            return Result.Failure(ContractErrors.NotPeriodStart);
        if (firstOpenPeriodStart is { } open && effectiveFrom < open)
            return Result.Failure(ContractErrors.PeriodAlreadyBilled);
        if (_rentTerms.Any(t => t.EffectiveFrom == effectiveFrom))
            return Result.Failure(ContractErrors.RentTermExists);

        _rentTerms.Add(new ContractRentTerm(Id, effectiveFrom, monthlyRent, addendumNo, note));
        return Result.Success();
    }

    // ------------------------------------------------------------------ Khoản thu (CT-UC-06)

    /// <summary>
    /// Đổi số lượng / giá riêng của một khoản thu (hoặc gắn thêm khoản mới) từ đầu một kỳ thu (CT-BR-06, cùng quy tắc khóa CT-BR-05).
    /// </summary>
    public Result ChangeFee(ContractFeeInput input, DateOnly effectiveFrom, DateOnly? firstOpenPeriodStart)
    {
        var check = CheckFeeChangeDate(input.FeeTypeId, effectiveFrom, firstOpenPeriodStart);
        if (check.IsFailure)
            return check;

        var sameDay = _fees.FirstOrDefault(f => f.FeeTypeId == input.FeeTypeId && f.EffectiveFrom == effectiveFrom);
        if (sameDay is not null)
        {
            sameDay.Replace(input);
            return Result.Success();
        }

        _fees.FirstOrDefault(f => f.FeeTypeId == input.FeeTypeId && f.Covers(effectiveFrom))?.EndOn(effectiveFrom.AddDays(-1));
        _fees.Add(new ContractFee(Id, PropertyId, input, effectiveFrom));
        return Result.Success();
    }

    /// <summary>Thôi tính khoản thu từ đầu kỳ <paramref name="effectiveFrom"/>.</summary>
    public Result RemoveFee(Guid feeTypeId, DateOnly effectiveFrom, DateOnly? firstOpenPeriodStart)
    {
        var check = CheckFeeChangeDate(feeTypeId, effectiveFrom, firstOpenPeriodStart);
        if (check.IsFailure)
            return check;

        var current = _fees.FirstOrDefault(f => f.FeeTypeId == feeTypeId && f.Covers(effectiveFrom));
        if (current is null)
            return Result.Failure(ContractErrors.FeeNotRegistered);
        if (current.EffectiveFrom == effectiveFrom)
            _fees.Remove(current);
        else
            current.EndOn(effectiveFrom.AddDays(-1));
        return Result.Success();
    }

    private Result CheckFeeChangeDate(Guid feeTypeId, DateOnly effectiveFrom, DateOnly? firstOpenPeriodStart)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (effectiveFrom < StartDate || (EndDate is { } end && effectiveFrom > end))
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (!BillingPeriodCalculator.IsPeriodStart(StartDate, BillingAnchorDay, effectiveFrom))
            return Result.Failure(ContractErrors.NotPeriodStart);
        if (firstOpenPeriodStart is { } open && effectiveFrom < open)
            return Result.Failure(ContractErrors.PeriodAlreadyBilled);
        if (_fees.Any(f => f.FeeTypeId == feeTypeId && f.EffectiveFrom > effectiveFrom))
            return Result.Failure(ContractErrors.FeeLaterChangeExists);
        return Result.Success();
    }

    public Result Extend(DateOnly newEndDate)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (EndDate is null)
            return Result.Failure(ContractErrors.CannotExtendIndefinite);
        if (newEndDate <= EndDate)
            return Result.Failure(ContractErrors.InvalidEndDate);

        EndDate = newEndDate;
        HoldoverSince = null; // CT-BR-45: đã ký phụ lục gia hạn ⇒ hết trạng thái ở tiếp chưa ký
        HoldoverNote = null;
        return Result.Success();
    }

    /// <summary>CT-UC-22: HĐ đã quá hạn, chủ trọ cho ở tiếp chưa ký lại — vẫn tính tiền theo điều khoản cũ.</summary>
    public Result StartHoldover(DateOnly today, string? note)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (EndDate is not { } end || end >= today)
            return Result.Failure(ContractErrors.NotExpired);
        if (HoldoverSince is not null)
            return Result.Failure(ContractErrors.HoldoverAlready);

        HoldoverSince = today;
        HoldoverNote = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }

    /// <summary>
    /// CT-UC-21: dữ liệu HĐ nháp mới cho người còn ở sau ngày bàn giao X (bắt đầu X+1): chép giá thuê hiện hành, kỳ thu, cọc (thông tin),
    /// văn bản, dịch vụ đang áp. Quan hệ người ở giữ nguyên nếu chủ hộ còn ở, ngược lại bỏ trống để khai lại với người đứng tên mới.
    /// </summary>
    public Result<ContractDraftData> ResignDraft(DateOnly handoverDate, Guid representativeRenterId, DateOnly? endDate)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure<ContractDraftData>(ContractErrors.NotActive);

        var start = handoverDate.AddDays(1);
        var staying = _occupants.Where(o => o.MoveOutDate is null || o.MoveOutDate > handoverDate).ToList();
        if (staying.Count == 0)
            return Result.Failure<ContractDraftData>(ContractErrors.ResignNoOccupantLeft);
        if (staying.All(o => o.RenterId != representativeRenterId))
            return Result.Failure<ContractDraftData>(ContractErrors.ResignRepresentativeNotOccupant);
        if (endDate is { } end && end <= start)
            return Result.Failure<ContractDraftData>(ContractErrors.InvalidEndDate);

        var keepRelations = staying.Any(o => o.RenterId == ReferenceRenterId);
        Guid? head = keepRelations && ReferenceRenterId != representativeRenterId ? ReferenceRenterId : null;
        var reference = head ?? representativeRenterId;
        var occupants = staying.Select(o => new OccupantInput(
                o.RenterId, start, o.ExpectedEndDate > start ? o.ExpectedEndDate : null,
                keepRelations ? o.Relationship : null, o.Note,
                keepRelations && o.RenterId != reference ? o.RelationshipType : null,
                keepRelations && o.GuardianConsent))
            .ToList();
        var fees = _fees.Where(f => f.Covers(start)).Select(f => new ContractFeeInput(f.FeeTypeId, f.Quantity, f.UnitPriceOverride)).ToList();
        var document = new ContractDocument(TemplateId, ContractType, Title ?? ContractTypes.DefaultTitle(ContractType), Clauses,
            CustomFieldDefinitions, CustomFieldValues);

        return new ContractDraftData(
            representativeRenterId, start, endDate, SignedDate: null, SignedPlace: null, EffectiveDate: null,
            CurrentRent(start) ?? _rentTerms.MaxBy(t => t.EffectiveFrom)!.MonthlyRent, DepositAmount, DepositTerms,
            BillingAnchorDay, ChargeMode, ProrationMode, PaymentDueDays, NoticeDays, PaymentMethods, CopiesCount, TermsText, Note: null,
            occupants, document, head, fees);
    }

    /// <summary>CT-UC-21: xe đang đăng ký của những người được chép sang HĐ mới (xe không gắn chủ cũng chép).</summary>
    public IReadOnlyList<VehicleInput> ResignVehicles(IReadOnlyCollection<Guid> renterIds, DateOnly start) =>
        _vehicles.Where(v => v.IsActive && (v.RenterId is null || renterIds.Contains(v.RenterId.Value)))
            .Select(v => new VehicleInput(v.RenterId, v.VehicleType, v.PlateNumber, v.BrandColor, start, v.Note))
            .ToList();

    /// <summary>CT-BR-16: báo trước ít hơn số ngày thỏa thuận → trả cảnh báo, không chặn.</summary>
    public Result<NoticeResult> GiveNotice(DateOnly noticeDate, DateOnly plannedMoveOutDate)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure<NoticeResult>(ContractErrors.NotActive);
        if (plannedMoveOutDate < noticeDate || plannedMoveOutDate < StartDate)
            return Result.Failure<NoticeResult>(ContractErrors.InvalidEndDate);

        NoticeGivenDate = noticeDate;
        PlannedMoveOutDate = plannedMoveOutDate;

        var actualDays = plannedMoveOutDate.DayNumber - noticeDate.DayNumber;
        return Result.Success(new NoticeResult(actualDays < NoticeDays, NoticeDays, actualDays));
    }

    // ------------------------------------------------------------------ Thanh lý

    public Result StartLiquidation(
        DateOnly actualEndDate, TerminationReason reason, TerminationGround? ground, string? note, DateOnly today)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        // HĐ không thời hạn: thông báo chấm dứt trước 90 ngày ⇒ được hẹn ngày trả phòng xa hơn mức thường (CT-BR-42).
        var maxDaysAhead = ground == Contracts.TerminationGround.IndefiniteTermNotice ? ContractWarnings.IndefiniteNoticeDays : MaxLiquidationDaysAhead;
        if (actualEndDate < StartDate
            || _occupants.Any(o => o.MoveInDate > actualEndDate)
            || actualEndDate > today.AddDays(maxDaysAhead))
            return Result.Failure(ContractErrors.InvalidEndDate);
        if (reason == Contracts.TerminationReason.LessorUnilateral && ground is null)
            return Result.Failure(ContractErrors.TerminationGroundRequired);
        if (reason == Contracts.TerminationReason.Expired && (EndDate is not { } end || actualEndDate < end))
            return Result.Failure(ContractErrors.ExpiredReasonInvalid);
        if (reason == Contracts.TerminationReason.LessorUnilateral && ground == Contracts.TerminationGround.IndefiniteTermNotice && EndDate is not null)
            return Result.Failure(ContractErrors.IndefiniteGroundOnly);
        if (reason == Contracts.TerminationReason.Abandoned && string.IsNullOrWhiteSpace(note))
            return Result.Failure(ContractErrors.AbandonedNoteRequired);

        ActualEndDate = actualEndDate;
        LiquidationStartedOn = today;
        TerminationReason = reason;
        TerminationGround = reason == Contracts.TerminationReason.LessorUnilateral ? ground : null;
        TerminationNote = TextNormalizer.TrimToNull(note);
        Status = ContractStatus.Liquidating;
        return Result.Success();
    }

    public Result CancelLiquidation()
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        ActualEndDate = null;
        TerminationReason = null;
        TerminationGround = null;
        TerminationNote = null;
        LiquidationStartedOn = null;
        Status = ContractStatus.Active;
        return Result.Success();
    }

    /// <summary>
    /// CT-BR-12 (phần đã có): đóng mọi người ở và xe còn mở tại ngày kết thúc thực tế.
    /// Điều kiện phiếu quyết toán / số dư cọc sẽ bổ sung khi có M07, M08.
    /// </summary>
    public Result CompleteLiquidation(DateTimeOffset now)
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        // Chỉ hoàn tất khi đã tới ngày trả phòng: trước đó người thuê vẫn đang ở (chỉ số cuối, bàn giao chưa có).
        var endDate = ActualEndDate!.Value;
        if (now.ToBusinessDate() < endDate)
            return Result.Failure(ContractErrors.LiquidationBeforeEndDate);

        foreach (var occupant in _occupants.Where(o => o.MoveOutDate is null || o.MoveOutDate > endDate))
            occupant.MoveOut(endDate);
        foreach (var vehicle in _vehicles.Where(v => v.IsActive))
            vehicle.End(endDate < vehicle.RegisteredFrom ? vehicle.RegisteredFrom : endDate);

        Status = ContractStatus.Ended;
        EndedAt = now;
        return Result.Success();
    }

    // ------------------------------------------------------------------ Tài sản bàn giao & xe

    public Result<ContractAsset> AddAsset(AssetInput input)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure<ContractAsset>(ContractErrors.NotDraft);

        var asset = new ContractAsset(Id, input);
        _assets.Add(asset);
        return Result.Success(asset);
    }

    public Result UpdateAsset(Guid assetId, AssetInput input)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);

        var asset = _assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
            return Result.Failure(ContractErrors.AssetNotFound);

        asset.Update(input);
        return Result.Success();
    }

    public Result RemoveAsset(Guid assetId)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);

        return _assets.RemoveAll(a => a.Id == assetId) == 1
            ? Result.Success()
            : Result.Failure(ContractErrors.AssetNotFound);
    }

    public Result RecordAssetReturn(Guid assetId, string? condition, decimal? compensationValue)
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        var asset = _assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
            return Result.Failure(ContractErrors.AssetNotFound);

        asset.RecordReturn(condition, compensationValue);
        return Result.Success();
    }

    public Result<ContractVehicle> RegisterVehicle(VehicleInput input)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Active))
            return Result.Failure<ContractVehicle>(ContractErrors.NotEditable);
        if (input.RegisteredFrom < StartDate)
            return Result.Failure<ContractVehicle>(ContractErrors.DateOutsideContract);

        var vehicle = new ContractVehicle(Id, input);
        _vehicles.Add(vehicle);
        return Result.Success(vehicle);
    }

    public Result EndVehicle(Guid vehicleId, DateOnly endDate)
    {
        var vehicle = _vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null)
            return Result.Failure(ContractErrors.VehicleNotFound);
        if (!vehicle.IsActive)
            return Result.Failure(ContractErrors.VehicleAlreadyEnded);
        if (endDate < vehicle.RegisteredFrom)
            return Result.Failure(ContractErrors.DateOutsideContract);

        vehicle.End(endDate);
        return Result.Success();
    }

    /// <summary>Số người ở đồng thời lớn nhất (quét theo ngày vào của từng người).</summary>
    /// <summary>Số người ở cùng lúc vượt sức chứa (CT-BR-09) — Application dùng để ghi audit khi chủ ý vượt.</summary>
    public bool ExceedsCapacity(int roomMaxOccupants) => MaxConcurrentOccupants() > roomMaxOccupants;

    private int MaxConcurrentOccupants() =>
        _occupants.Count == 0 ? 0 : _occupants.Max(o => _occupants.Count(other => other.IsStayingOn(o.MoveInDate)));
}

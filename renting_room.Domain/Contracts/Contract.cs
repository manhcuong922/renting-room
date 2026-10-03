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

    public bool IsOverdue(DateOnly today) => Status == ContractStatus.Active && EndDate < today;

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

    public Result Extend(DateOnly newEndDate)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (EndDate is null)
            return Result.Failure(ContractErrors.CannotExtendIndefinite);
        if (newEndDate <= EndDate)
            return Result.Failure(ContractErrors.InvalidEndDate);

        EndDate = newEndDate;
        return Result.Success();
    }

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
        if (actualEndDate < StartDate
            || _occupants.Any(o => o.MoveInDate > actualEndDate)
            || actualEndDate > today.AddDays(MaxLiquidationDaysAhead))
            return Result.Failure(ContractErrors.InvalidEndDate);
        if (reason == Contracts.TerminationReason.LessorUnilateral && ground is null)
            return Result.Failure(ContractErrors.TerminationGroundRequired);

        ActualEndDate = actualEndDate;
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

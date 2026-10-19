using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>
/// Hợp đồng thuê 1 phòng. Vòng đời: Draft → Active → Liquidating → Ended (hoặc Draft → Cancelled).
/// Giá thuê, người ở, tài sản, xe là entity con — mọi thay đổi đi qua method của aggregate để giữ bất biến.
/// </summary>
public sealed partial class Contract : TenantEntity
{
    public const int MaxStartDaysAhead = 1;
    public const int MaxLiquidationDaysAhead = 60;

    private readonly List<ContractRentTerm> _rentTerms = [];
    private readonly List<ContractOccupant> _occupants = [];
    private readonly List<ContractAsset> _assets = [];
    private readonly List<ContractVehicle> _vehicles = [];
    private readonly List<ContractFee> _fees = [];
    private readonly List<ContractRoomMove> _roomMoves = [];
    private readonly List<ContractRepresentative> _representativeHistory = [];

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

    /// <summary>CT-BR-14: ngày vào phòng hiện tại — = ngày bắt đầu, đổi khi chuyển phòng. Ràng buộc "1 phòng 1 HĐ" tính từ ngày này.</summary>
    public DateOnly RoomSince { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateOnly? ActualEndDate { get; private set; }
    public DateOnly? NoticeGivenDate { get; private set; }
    public DateOnly? PlannedMoveOutDate { get; private set; }
    public int NoticeDays { get; private set; }
    public decimal DepositAmount { get; private set; }
    public string? DepositTerms { get; private set; }

    /// <summary>M08 PM-BR-32: cọc chỉ theo dõi — đang giữ / đã hoàn trả / đã chuyển sang HĐ ký lại.</summary>
    public DepositStatus DepositStatus { get; private set; } = DepositStatus.Holding;
    public DateOnly? DepositRefundedOn { get; private set; }
    public decimal? DepositRefundedAmount { get; private set; }
    public string? DepositNote { get; private set; }

    public bool HasDeposit => DepositAmount > 0;
    /// <summary>
    /// K5 — "Tính tiền từ ngày" (C-05): kỳ đầu bắt đầu từ ngày này, những ngày trước không tính tiền (HĐ nhập từ sổ cũ, cho ở miễn phí vài
    /// ngày đầu). Mặc định = ngày bắt đầu. Ngày chốt / thu trước–thu sau / tính kỳ lẻ là của khu (CT-BR-04).
    /// </summary>
    public DateOnly BillingStartDate { get; private set; }
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

    /// <summary>Đã có bản HĐ ký (giấy cất ở đâu / ảnh / PDF) — chưa có ⇒ cờ "Thiếu tài liệu". Không ảnh hưởng thu tiền.</summary>
    public bool HasSignedDocument { get; private set; }
    public string? SignedDocumentNote { get; private set; }
    public string? UtilityPriceSnapshot { get; private set; }

    public TerminationReason? TerminationReason { get; private set; }

    /// <summary>Ngày ghi nhận bắt đầu thanh lý — đối chiếu thời hạn báo trước khi bên cho thuê đơn phương chấm dứt (CT-BR-21).</summary>
    public DateOnly? LiquidationStartedOn { get; private set; }

    /// <summary>CT-BR-45: ngày chủ trọ chọn "ở tiếp, chưa ký lại" sau khi HĐ hết hạn; gia hạn thì xóa.</summary>
    public DateOnly? HoldoverSince { get; private set; }
    public string? HoldoverNote { get; private set; }

    /// <summary>CT-BR-45: ngày hết hạn trước khi chủ trọ chọn "ở tiếp" (HĐ khi đó chuyển sang không thời hạn); xóa khi gia hạn.</summary>
    public DateOnly? PreviousEndDate { get; private set; }
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

    /// <summary>
    /// RT-BR-06: ẩn danh người thuê — xóa biển số (hiệu – màu) xe đã kết thúc: mọi xe nếu <paramref name="ownerRenterId"/> null (HĐ người đó
    /// đứng tên), ngược lại chỉ xe người đó là chủ. Trả id các xe đã xóa.
    /// </summary>
    public IReadOnlyList<Guid> EraseEndedVehicleIdentity(Guid? ownerRenterId) =>
        _vehicles.Where(v => ownerRenterId is null || v.RenterId == ownerRenterId).Where(v => v.EraseIdentity()).Select(v => v.Id).ToList();
    public IReadOnlyList<ContractFee> Fees => _fees;
    public IReadOnlyList<ContractRoomMove> RoomMoves => _roomMoves;

    /// <summary>CT-BR-51: những người đã thôi đứng tên HĐ (đổi người đứng tên, HĐ giữ nguyên).</summary>
    public IReadOnlyList<ContractRepresentative> RepresentativeHistory => _representativeHistory;

    /// <summary>Ngày người đứng tên hiện tại bắt đầu đứng tên; null = từ ngày bắt đầu HĐ.</summary>
    public DateOnly? RepresentativeSince { get; private set; }

    /// <summary>Các phòng HĐ đã / đang ở theo thời gian (CT-BR-47 — tính điện nước kỳ có chuyển phòng).</summary>
    public IReadOnlyList<RoomStay> RoomStays =>
        [.. _roomMoves.OrderBy(m => m.FromDate).Select(m => new RoomStay(m.RoomId, m.FromDate, m.ToDate)), new RoomStay(RoomId, RoomSince, ActualEndDate)];

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
            flags.Add(ContractFlag.ExpiredAwaitingDecision);
        if (!HasSignedDocument)
            flags.Add(ContractFlag.MissingSignedDocument);
        return flags;
    }

    public decimal? CurrentRent(DateOnly date) =>
        _rentTerms.Where(t => t.EffectiveFrom <= date).MaxBy(t => t.EffectiveFrom)?.MonthlyRent;

    public IEnumerable<ContractOccupant> OccupantsOn(DateOnly date) => _occupants.Where(o => o.IsStayingOn(date));

    /// <summary>Kỳ của HĐ (C-05) theo lịch kỳ thu của khu, từ "Tính tiền từ ngày" tới ngày trả phòng.</summary>
    public IReadOnlyList<BillingPeriod> BillingPeriods(BillingSchedule schedule, DateOnly until) =>
        schedule.ContractPeriods(BillingStartDate, ActualEndDate, until);

    public bool IsPeriodStart(BillingSchedule schedule, DateOnly date) => BillingPeriods(schedule, date).Any(p => p.Start == date);

    /// <summary>BL-BR-20: dữ liệu tính tiền đổi ⇒ báo để nháp đã lập của HĐ được đánh dấu "Cần tính lại".</summary>
    private void BillingChanged() => Raise(new ContractBillingChanged(Id));

    private void BillingChangedIf(bool changed)
    {
        if (changed)
            BillingChanged();
    }
}

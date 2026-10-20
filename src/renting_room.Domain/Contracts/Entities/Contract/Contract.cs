using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

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

    public DateOnly RoomSince { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateOnly? ActualEndDate { get; private set; }
    public DateOnly? NoticeGivenDate { get; private set; }
    public DateOnly? PlannedMoveOutDate { get; private set; }
    public int NoticeDays { get; private set; }
    public decimal DepositAmount { get; private set; }
    public string? DepositTerms { get; private set; }

    public DepositStatus DepositStatus { get; private set; } = DepositStatus.Holding;
    public DateOnly? DepositRefundedOn { get; private set; }
    public decimal? DepositRefundedAmount { get; private set; }
    public string? DepositNote { get; private set; }

    public bool HasDeposit => DepositAmount > 0;
    public DateOnly BillingStartDate { get; private set; }
    public PaymentMethod[] PaymentMethods { get; private set; } = [];
    public int CopiesCount { get; private set; }
    public string? TermsText { get; private set; }
    public string? Note { get; private set; }
    public Guid? PreviousContractId { get; private set; }

    public Guid? TemplateId { get; private set; }
    public ContractType ContractType { get; private set; }
    public string? Title { get; private set; }
    public string? Clauses { get; private set; }
    public string? CustomFieldDefinitions { get; private set; }
    public string? CustomFieldValues { get; private set; }

    public Guid? HouseholdHeadRenterId { get; private set; }

    public Guid ReferenceRenterId => HouseholdHeadRenterId ?? RepresentativeRenterId;

    public string? SigningSnapshot { get; private set; }
    public string? HouseRulesSnapshot { get; private set; }

    public bool HasSignedDocument { get; private set; }
    public string? SignedDocumentNote { get; private set; }
    public string? UtilityPriceSnapshot { get; private set; }

    public TerminationReason? TerminationReason { get; private set; }

    public DateOnly? LiquidationStartedOn { get; private set; }

    public DateOnly? HoldoverSince { get; private set; }
    public string? HoldoverNote { get; private set; }

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

    public IReadOnlyList<Guid> EraseEndedVehicleIdentity(Guid? ownerRenterId) =>
        _vehicles.Where(v => ownerRenterId is null || v.RenterId == ownerRenterId).Where(v => v.EraseIdentity()).Select(v => v.Id).ToList();
    public IReadOnlyList<ContractFee> Fees => _fees;
    public IReadOnlyList<ContractRoomMove> RoomMoves => _roomMoves;

    public IReadOnlyList<ContractRepresentative> RepresentativeHistory => _representativeHistory;

    /// <summary>Ngày người đứng tên hiện tại bắt đầu đứng tên; null = từ ngày bắt đầu HĐ.</summary>
    public DateOnly? RepresentativeSince { get; private set; }

    public IReadOnlyList<RoomStay> RoomStays =>
        [.. _roomMoves.OrderBy(m => m.FromDate).Select(m => new RoomStay(m.RoomId, m.FromDate, m.ToDate)), new RoomStay(RoomId, RoomSince, ActualEndDate)];

    public bool IsOverdue(DateOnly today) => Status == ContractStatus.Active && EndDate < today;

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

    public IReadOnlyList<BillingPeriod> BillingPeriods(BillingSchedule schedule, DateOnly until) =>
        schedule.ContractPeriods(BillingStartDate, ActualEndDate, until);

    public bool IsPeriodStart(BillingSchedule schedule, DateOnly date) => BillingPeriods(schedule, date).Any(p => p.Start == date);

    private void BillingChanged() => Raise(new ContractBillingChanged(Id));

    private void BillingChangedIf(bool changed)
    {
        if (changed)
            BillingChanged();
    }
}

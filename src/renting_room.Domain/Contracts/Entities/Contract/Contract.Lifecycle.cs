using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    public static Contract CreateDraft(Guid propertyId, Guid roomId, string contractNo, ContractDraftData data, Guid? previousContractId = null)
    {
        if (propertyId == Guid.Empty || roomId == Guid.Empty)
            throw new ArgumentException("Property and room are required.");
        if (string.IsNullOrWhiteSpace(contractNo))
            throw new ArgumentException("Contract number is required.", nameof(contractNo));

        var contract = new Contract
        {
            Id = Guid.CreateVersion7(),
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
        if (data.BillingStartDate is { } billingStart && (billingStart < data.StartDate || (data.EndDate is { } last && billingStart > last)))
            throw new ArgumentException("Billing start date must be within the contract.");
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
        RoomSince = data.StartDate;
        EndDate = data.EndDate;
        SignedDate = data.SignedDate;
        SignedPlace = TextNormalizer.TrimToNull(data.SignedPlace);
        EffectiveDate = data.EffectiveDate;
        DepositAmount = data.DepositAmount;
        DepositTerms = TextNormalizer.TrimToNull(data.DepositTerms);
        BillingStartDate = data.BillingStartDate ?? data.StartDate;
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

        foreach (var vehicle in _vehicles.Where(v => v.IsActive && v.RegisteredFrom < StartDate))
            vehicle.MoveStart(StartDate);

        _occupants.Clear();
        _occupants.AddRange(data.Occupants.Select(o => new ContractOccupant(Id, WithRole(o))));
        BillingChanged();
    }

    public Result Activate(ActivationContext context)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);
        if (!context.RoomAvailable)
            return Result.Failure(ContractErrors.RoomUnavailable);
        if (StartDate > context.Today.AddDays(MaxStartDaysAhead))
            return Result.Failure(ContractErrors.StartDateTooFarInFuture);
        if (_occupants.Count == 0)
            return Result.Failure(ContractErrors.NoOccupant);

        var defaultSignedDate = StartDate < context.Today ? StartDate : context.Today;
        var signedDate = SignedDate
            ?? (EffectiveDate is { } agreedEffective && agreedEffective < defaultSignedDate ? agreedEffective : defaultSignedDate);

        SignedDate = signedDate;
        EffectiveDate ??= signedDate;
        SigningSnapshot = context.SigningSnapshotJson;
        HouseRulesSnapshot = context.HouseRulesSnapshot;
        UtilityPriceSnapshot = context.UtilityPriceSnapshotJson;
        Status = ContractStatus.Active;
        ActivatedAt = context.Now;
        return Result.Success();
    }

    public Result SetSignedDocument(bool hasSignedDocument, string? note)
    {
        if (Status == ContractStatus.Cancelled)
            return Result.Failure(ContractErrors.NotEditable);
        HasSignedDocument = hasSignedDocument;
        SignedDocumentNote = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }
}

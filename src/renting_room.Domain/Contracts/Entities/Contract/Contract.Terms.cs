using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    public Result ChangeRent(
        DateOnly effectiveFrom, decimal monthlyRent, string? addendumNo, string? note, DateOnly? firstOpenPeriodStart, BillingSchedule schedule)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (effectiveFrom < StartDate)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (effectiveFrom != StartDate && !IsPeriodStart(schedule, effectiveFrom))
            return Result.Failure(ContractErrors.NotPeriodStart);
        if (firstOpenPeriodStart is { } open && effectiveFrom < open)
            return Result.Failure(ContractErrors.PeriodAlreadyBilled);

        if (_rentTerms.FirstOrDefault(t => t.EffectiveFrom == effectiveFrom) is { } sameDay)
        {
            BillingChangedIf(sameDay.Replace(monthlyRent, addendumNo, note));
            return Result.Success();
        }
        _rentTerms.Add(new ContractRentTerm(Id, effectiveFrom, monthlyRent, addendumNo, note));
        BillingChanged();
        return Result.Success();
    }

    public Result Extend(DateOnly newEndDate)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if ((EndDate ?? (HoldoverSince is null ? null : PreviousEndDate)) is not { } currentEnd)
            return Result.Failure(ContractErrors.CannotExtendIndefinite);
        if (newEndDate <= currentEnd)
            return Result.Failure(ContractErrors.InvalidEndDate);

        EndDate = newEndDate;
        HoldoverSince = null; // đã ký phụ lục gia hạn ⇒ hết trạng thái ở tiếp chưa ký
        HoldoverNote = null;
        PreviousEndDate = null;
        return Result.Success();
    }

    public Result StartHoldover(DateOnly today, string? note)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (HoldoverSince is not null)
            return Result.Failure(ContractErrors.HoldoverAlready);
        if (EndDate is not { } end || end >= today)
            return Result.Failure(ContractErrors.NotExpired);

        HoldoverSince = today;
        HoldoverNote = TextNormalizer.TrimToNull(note);
        PreviousEndDate = end;
        EndDate = null;
        return Result.Success();
    }

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
                keepRelations && o.GuardianConsent,
                o.RenterId == representativeRenterId ? OccupantRole.Resident : o.Role))
            .ToList();
        var fees = _fees.Where(f => f.Covers(start)).Select(f => new ContractFeeInput(f.FeeTypeId, f.Quantity, f.UnitPriceOverride)).ToList();
        var document = new ContractDocument(TemplateId, ContractType, Title ?? ContractTypes.DefaultTitle(ContractType), Clauses,
            CustomFieldDefinitions, CustomFieldValues);

        return new ContractDraftData(
            representativeRenterId, start, endDate, SignedDate: null, SignedPlace: null, EffectiveDate: null,
            CurrentRent(start) ?? _rentTerms.MaxBy(t => t.EffectiveFrom)!.MonthlyRent, DepositAmount, DepositTerms,
            NoticeDays, PaymentMethods, CopiesCount, TermsText, Note: null,
            occupants, document, head, fees);
    }

    public IReadOnlyList<VehicleInput> ResignVehicles(IReadOnlyCollection<Guid> renterIds, DateOnly handoverDate, DateOnly start, Guid newContractId)
    {
        var moving = _vehicles.Where(v => v.IsActive && (v.RenterId is null || renterIds.Contains(v.RenterId.Value))).ToList();
        foreach (var vehicle in moving)
            vehicle.EndForResign(handoverDate < vehicle.RegisteredFrom ? vehicle.RegisteredFrom : handoverDate, newContractId);
        return moving.Select(v => new VehicleInput(v.RenterId, v.VehicleType, v.PlateNumber, v.BrandColor, start, v.Note)).ToList();
    }

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

    /// <summary>CT-UC-07: người thuê đổi ý ở tiếp — bỏ ngày báo / dự kiến trả ⇒ hóa đơn tháng lập lại được.</summary>
    public Result WithdrawNotice()
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (PlannedMoveOutDate is null)
            return Result.Failure(ContractErrors.NoticeNotGiven);
        NoticeGivenDate = null;
        PlannedMoveOutDate = null;
        return Result.Success();
    }

    /// <summary>Nhãn "Trả phòng dd/MM": ngày trả phòng khi đang thanh lý, ngày dự kiến trả đã báo khi còn hiệu lực.</summary>
    public DateOnly? MoveOutDate => Status switch
    {
        ContractStatus.Liquidating => ActualEndDate,
        ContractStatus.Active => PlannedMoveOutDate,
        _ => null
    };
}

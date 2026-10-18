using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    // Phụ lục: giá, gia hạn, báo trả phòng
    /// <summary>CT-BR-05: giá mới chỉ áp từ đầu một kỳ và từ kỳ chưa lập phiếu.</summary>
    public Result ChangeRent(
        DateOnly effectiveFrom, decimal monthlyRent, string? addendumNo, string? note, DateOnly? firstOpenPeriodStart, BillingSchedule schedule)
    {
        // Nháp sửa giá trực tiếp bằng UpdateDraft; phụ lục chỉ có ý nghĩa với hợp đồng đã ký.
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (effectiveFrom < StartDate) // sau ngày hết hạn vẫn đổi được (CT-BR-37)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (effectiveFrom != StartDate && !IsPeriodStart(schedule, effectiveFrom))
            return Result.Failure(ContractErrors.NotPeriodStart);
        if (firstOpenPeriodStart is { } open && effectiveFrom < open)
            return Result.Failure(ContractErrors.PeriodAlreadyBilled);

        // CT-UC-05: sửa trực tiếp — cùng ngày áp dụng (VD kỳ đầu chưa chốt) ⇒ thay giá đó, không tạo thêm bản giá.
        if (_rentTerms.FirstOrDefault(t => t.EffectiveFrom == effectiveFrom) is { } sameDay)
        {
            sameDay.Replace(monthlyRent, addendumNo, note);
            return Result.Success();
        }
        _rentTerms.Add(new ContractRentTerm(Id, effectiveFrom, monthlyRent, addendumNo, note));
        return Result.Success();
    }

    public Result Extend(DateOnly newEndDate)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        // CT-BR-45: HĐ đang ở tiếp (đã thành không thời hạn) vẫn ký phụ lục gia hạn được — mốc so sánh là hạn cũ.
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

    /// <summary>
    /// CT-UC-22 / CT-BR-45: HĐ đã quá hạn, chủ trọ cho ở tiếp chưa ký lại ⇒ HĐ thành không thời hạn (giữ hạn cũ ở <see cref="PreviousEndDate"/>),
    /// vẫn tính tiền theo điều khoản cũ, hết nhãn "hết hạn chờ quyết định".
    /// </summary>
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

    /// <summary>CT-UC-21: xe đang đăng ký của những người được chép sang HĐ mới (xe không gắn chủ cũng chép).</summary>
    /// <summary>
    /// CT-UC-21: xe của người sang HĐ ký lại (và xe không gắn người) — kết thúc đăng ký trên HĐ này tại ngày bàn giao (một biển số chỉ đăng ký ở
    /// một nơi), trả về bản đăng ký cho HĐ mới từ <paramref name="start"/>.
    /// </summary>
    public IReadOnlyList<VehicleInput> ResignVehicles(IReadOnlyCollection<Guid> renterIds, DateOnly handoverDate, DateOnly start)
    {
        var moving = _vehicles.Where(v => v.IsActive && (v.RenterId is null || renterIds.Contains(v.RenterId.Value))).ToList();
        foreach (var vehicle in moving)
            vehicle.End(handoverDate < vehicle.RegisteredFrom ? vehicle.RegisteredFrom : handoverDate);
        return moving.Select(v => new VehicleInput(v.RenterId, v.VehicleType, v.PlateNumber, v.BrandColor, start, v.Note)).ToList();
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
}

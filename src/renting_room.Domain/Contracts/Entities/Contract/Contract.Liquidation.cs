using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    public Result StartLiquidation(
        DateOnly actualEndDate, TerminationReason reason, TerminationGround? ground, string? note, DateOnly today,
        FinalPeriodCharge? finalPeriodCharge = null)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        var maxDaysAhead = ground == Contracts.TerminationGround.IndefiniteTermNotice ? ContractWarnings.IndefiniteNoticeDays : MaxLiquidationDaysAhead;
        if (actualEndDate < StartDate
            || _occupants.Any(o => o.MoveInDate > actualEndDate)
            || actualEndDate > today.AddDays(maxDaysAhead))
            return Result.Failure(ContractErrors.InvalidEndDate);
        if (actualEndDate < RoomSince)
            return Result.Failure(ContractErrors.EndDateBeforeRoomTransfer);
        if (reason == Contracts.TerminationReason.Expired && (EndDate is not { } end || actualEndDate < end))
            return Result.Failure(ContractErrors.ExpiredReasonInvalid);
        if (reason == Contracts.TerminationReason.LessorUnilateral && ground == Contracts.TerminationGround.IndefiniteTermNotice && EndDate is not null)
            return Result.Failure(ContractErrors.IndefiniteGroundOnly);
        if (reason == Contracts.TerminationReason.Abandoned && string.IsNullOrWhiteSpace(note))
            return Result.Failure(ContractErrors.AbandonedNoteRequired);

        ActualEndDate = actualEndDate;
        BillingChanged();
        LiquidationStartedOn = today;
        TerminationReason = reason;
        TerminationGround = reason == Contracts.TerminationReason.LessorUnilateral ? ground : null;
        TerminationNote = TextNormalizer.TrimToNull(note);
        FinalPeriodCharge = finalPeriodCharge;
        Status = ContractStatus.Liquidating;
        return Result.Success();
    }

    /// <summary>CT-BR-11: đổi cách tính kỳ cuối khi hóa đơn trả phòng chưa chốt (người gọi kiểm và tính lại nháp).</summary>
    public Result ChangeFinalPeriodCharge(FinalPeriodCharge? charge)
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);
        FinalPeriodCharge = charge;
        return Result.Success();
    }

    public Result CancelLiquidation()
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        ActualEndDate = null;
        BillingChanged();
        TerminationReason = null;
        TerminationGround = null;
        TerminationNote = null;
        LiquidationStartedOn = null;
        FinalPeriodCharge = null;
        Status = ContractStatus.Active;
        return Result.Success();
    }

    public void ReplaceSigningSnapshot(string? signingSnapshot) => SigningSnapshot = signingSnapshot;

    public Result CompleteLiquidation(DateTimeOffset now)
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        var endDate = ActualEndDate!.Value;
        if (now.ToBusinessDate() < endDate)
            return Result.Failure(ContractErrors.LiquidationBeforeEndDate);

        foreach (var occupant in _occupants.Where(o => o.MoveOutDate is null || o.MoveOutDate > endDate))
            BillingChangedIf(occupant.MoveOut(endDate));
        foreach (var vehicle in _vehicles.Where(v => v.IsActive))
            vehicle.End(endDate < vehicle.RegisteredFrom ? vehicle.RegisteredFrom : endDate);

        Status = ContractStatus.Ended;
        EndedAt = now;
        return Result.Success();
    }
}

using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
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
        // CT-BR-11: đã chuyển phòng ⇒ không trả phòng trước ngày vào phòng hiện tại (khoảng ở phòng hiện tại phải hợp lệ).
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
        Status = ContractStatus.Liquidating;
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
        Status = ContractStatus.Active;
        return Result.Success();
    }

    /// <summary>
    /// CT-BR-12 (phần đã có): đóng mọi người ở và xe còn mở tại ngày kết thúc thực tế.
    /// Điều kiện phiếu quyết toán / số dư cọc sẽ bổ sung khi có M07, M08.
    /// </summary>
    /// <summary>RT-BR-06: thay bản chụp lúc ký bằng bản đã xóa dữ liệu cá nhân bên thuê — ngoại lệ duy nhất cho bản chụp bất biến.</summary>
    public void ReplaceSigningSnapshot(string? signingSnapshot) => SigningSnapshot = signingSnapshot;

    public Result CompleteLiquidation(DateTimeOffset now)
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        // Chỉ hoàn tất khi đã tới ngày trả phòng: trước đó người thuê vẫn đang ở (chỉ số cuối, bàn giao chưa có).
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

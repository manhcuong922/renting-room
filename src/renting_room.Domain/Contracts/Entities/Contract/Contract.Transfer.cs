using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    /// <summary>
    /// CT-UC-12 / CT-BR-14: HĐ đi theo người thuê sang phòng khác cùng khu từ ngày <paramref name="date"/> — người ở, xe, dịch vụ, cọc giữ nguyên.
    /// Phòng mới trống, chỉ số công tơ: Application kiểm và ghi.
    /// </summary>
    public Result TransferRoom(Guid newRoomId, DateOnly date, DateOnly today)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (newRoomId == RoomId)
            return Result.Failure(ContractErrors.TransferSameRoom);
        if (date <= RoomSince || date > today)
            return Result.Failure(ContractErrors.TransferInvalidDate);

        _roomMoves.Add(new ContractRoomMove(Id, RoomId, RoomSince, date));
        RoomId = newRoomId;
        RoomSince = date;
        BillingChanged();
        return Result.Success();
    }

    /// <summary>CT-BR-14: hủy lần chuyển phòng gần nhất (nhập nhầm phòng / ngày) — HĐ trở lại phòng trước đó như chưa từng chuyển.</summary>
    public Result CancelLastTransfer()
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (_roomMoves.MaxBy(m => m.FromDate) is not { } last)
            return Result.Failure(ContractErrors.TransferNothingToCancel);

        _roomMoves.Remove(last);
        RoomId = last.RoomId;
        RoomSince = last.FromDate;
        BillingChanged();
        return Result.Success();
    }

    /// <summary>CT-BR-14: lưu những gì lần chuyển phòng gần nhất đã đổi ngoài phòng + chỉ số, để hủy chuyển phòng hoàn nguyên được.</summary>
    public Result RecordTransferUndo(string snapshotJson)
    {
        if (_roomMoves.MaxBy(m => m.FromDate) is not { } last)
            return Result.Failure(ContractErrors.TransferNothingToCancel);
        BillingChangedIf(last.SetUndoSnapshot(snapshotJson));
        return Result.Success();
    }

    /// <summary>
    /// CT-BR-14: hủy chuyển phòng — trả lại mốc giá thuê lần chuyển đã ghi (xóa mốc mới thêm, hoặc trả mốc bị sửa đè về giá cũ).
    /// false = mốc đó đã bị đổi tiếp sau lần chuyển ⇒ giữ nguyên, chủ trọ tự kiểm.
    /// </summary>
    public bool RevertTransferRent(DateOnly effectiveFrom, decimal rentAfter, decimal? rentBefore, bool termAdded)
    {
        var term = _rentTerms.FirstOrDefault(t => t.EffectiveFrom == effectiveFrom);
        if (term is null || term.MonthlyRent != rentAfter)
            return false;
        if (termAdded && _rentTerms.Count > 1)
        {
            _rentTerms.Remove(term);
            BillingChanged();
        }
        else if (!termAdded && rentBefore is { } before)
            BillingChangedIf(term.Replace(before, term.AddendumNo, null));
        else
            return false;
        return true;
    }

    /// <summary>CT-BR-14: hủy chuyển phòng — gắn lại dịch vụ đã bỏ lúc chuyển. false = khoản đó đã được gắn / đổi lại sau lần chuyển.</summary>
    public bool RestoreTransferFee(ContractFeeInput input, DateOnly effectiveFrom)
    {
        if (_fees.Any(f => f.FeeTypeId == input.FeeTypeId && (f.Covers(effectiveFrom) || f.EffectiveFrom > effectiveFrom)))
            return false;
        var ended = _fees.FirstOrDefault(f => f.FeeTypeId == input.FeeTypeId && f.EffectiveTo == effectiveFrom.AddDays(-1)
            && f.Quantity == input.Quantity && f.UnitPriceOverride == input.UnitPriceOverride);
        if (ended is not null)
            BillingChangedIf(ended.Reopen());
        else
        {
            _fees.Add(new ContractFee(Id, PropertyId, input, effectiveFrom));
            BillingChanged();
        }
        return true;
    }
}

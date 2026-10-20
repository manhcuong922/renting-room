using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
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

    public Result RecordTransferUndo(string snapshotJson)
    {
        if (_roomMoves.MaxBy(m => m.FromDate) is not { } last)
            return Result.Failure(ContractErrors.TransferNothingToCancel);
        BillingChangedIf(last.SetUndoSnapshot(snapshotJson));
        return Result.Success();
    }

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

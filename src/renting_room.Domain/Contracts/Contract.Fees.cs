using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    // Khoản thu (CT-UC-06)
    /// <summary>
    /// Đổi số lượng / giá riêng của một khoản thu (hoặc gắn thêm khoản mới) từ đầu một kỳ thu (CT-BR-06, cùng quy tắc khóa CT-BR-05).
    /// </summary>
    public Result ChangeFee(ContractFeeInput input, DateOnly effectiveFrom, DateOnly? firstOpenPeriodStart, BillingSchedule schedule)
    {
        var check = CheckFeeChangeDate(input.FeeTypeId, effectiveFrom, firstOpenPeriodStart, schedule);
        if (check.IsFailure)
            return check;

        var sameDay = _fees.FirstOrDefault(f => f.FeeTypeId == input.FeeTypeId && f.EffectiveFrom == effectiveFrom);
        if (sameDay is not null)
        {
            sameDay.Replace(input);
            return Result.Success();
        }

        _fees.FirstOrDefault(f => f.FeeTypeId == input.FeeTypeId && f.Covers(effectiveFrom))?.EndOn(effectiveFrom.AddDays(-1));
        _fees.Add(new ContractFee(Id, PropertyId, input, effectiveFrom));
        return Result.Success();
    }

    /// <summary>Thôi tính khoản thu từ đầu kỳ <paramref name="effectiveFrom"/>.</summary>
    public Result RemoveFee(Guid feeTypeId, DateOnly effectiveFrom, DateOnly? firstOpenPeriodStart, BillingSchedule schedule)
    {
        var check = CheckFeeChangeDate(feeTypeId, effectiveFrom, firstOpenPeriodStart, schedule);
        if (check.IsFailure)
            return check;

        var current = _fees.FirstOrDefault(f => f.FeeTypeId == feeTypeId && f.Covers(effectiveFrom));
        if (current is null)
            return Result.Failure(ContractErrors.FeeNotRegistered);
        if (current.EffectiveFrom == effectiveFrom)
            _fees.Remove(current);
        else
            current.EndOn(effectiveFrom.AddDays(-1));
        return Result.Success();
    }

    private Result CheckFeeChangeDate(Guid feeTypeId, DateOnly effectiveFrom, DateOnly? firstOpenPeriodStart, BillingSchedule schedule)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(ContractErrors.NotActive);
        if (effectiveFrom < StartDate) // sau ngày hết hạn vẫn đổi được (CT-BR-37)
            return Result.Failure(ContractErrors.DateOutsideContract);
        if (effectiveFrom != StartDate && !IsPeriodStart(schedule, effectiveFrom))
            return Result.Failure(ContractErrors.NotPeriodStart);
        if (firstOpenPeriodStart is { } open && effectiveFrom < open)
            return Result.Failure(ContractErrors.PeriodAlreadyBilled);
        if (_fees.Any(f => f.FeeTypeId == feeTypeId && f.EffectiveFrom > effectiveFrom))
            return Result.Failure(ContractErrors.FeeLaterChangeExists);
        return Result.Success();
    }
}

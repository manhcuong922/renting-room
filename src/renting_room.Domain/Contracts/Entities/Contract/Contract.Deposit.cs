using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    public Result RefundDeposit(DateOnly refundedOn, decimal? amount, string? note, DateOnly today)
    {
        if (!HasDeposit)
            return Result.Failure(ContractErrors.NoDeposit);
        if (DepositStatus != DepositStatus.Holding)
            return Result.Failure(ContractErrors.DepositAlreadyRefunded);
        if (refundedOn > today)
            return Result.Failure(ContractErrors.InvalidRefundDate);
        var value = amount ?? DepositAmount;
        var trimmed = TextNormalizer.TrimToNull(note);
        if (value < 0 || value > DepositAmount || decimal.Round(value, 0) != value || (value < DepositAmount && trimmed is null))
            return Result.Failure(ContractErrors.InvalidRefundAmount);

        DepositStatus = DepositStatus.Refunded;
        DepositRefundedOn = refundedOn;
        DepositRefundedAmount = value;
        DepositNote = trimmed;
        return Result.Success();
    }

    public Result CancelDepositRefund()
    {
        if (DepositStatus == DepositStatus.Holding)
            return Result.Failure(ContractErrors.DepositNotRefunded);
        DepositStatus = DepositStatus.Holding;
        DepositRefundedOn = null;
        DepositRefundedAmount = null;
        DepositNote = null;
        return Result.Success();
    }

    public void MarkDepositTransferred(string newContractNo)
    {
        if (!HasDeposit || DepositStatus != DepositStatus.Holding)
            return;
        DepositStatus = DepositStatus.Transferred;
        DepositNote = TransferredNote(newContractNo);
    }

    public void RevertDepositTransfer(string? newContractNo = null)
    {
        if (DepositStatus != DepositStatus.Transferred || (newContractNo is not null && DepositNote != TransferredNote(newContractNo)))
            return;
        DepositStatus = DepositStatus.Holding;
        DepositNote = null;
    }

    private static string TransferredNote(string newContractNo) => $"Chuyển sang hợp đồng {newContractNo}";
}

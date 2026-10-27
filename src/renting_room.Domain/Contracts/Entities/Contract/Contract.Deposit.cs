using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    public Result RefundDeposit(DateOnly refundedOn, decimal? amount, string? note, DateOnly today)
    {
        if (!HasDeposit)
            return Result.Failure(ContractErrors.NoDeposit);
        if (DepositStatus == DepositStatus.Deducted)
            return Result.Failure(ContractErrors.DepositDeducted);
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

    /// <summary>PM-BR-33 "Không trả cọc": cọc đang giữ ⇒ không hoàn, ghi ngày + lý do.</summary>
    public Result ForfeitDeposit(DateOnly date, string? note, DateOnly today)
    {
        if (!HasDeposit)
            return Result.Failure(ContractErrors.NoDeposit);
        if (DepositStatus == DepositStatus.Deducted)
            return Result.Failure(ContractErrors.DepositDeducted);
        if (DepositStatus != DepositStatus.Holding)
            return Result.Failure(ContractErrors.DepositAlreadyRefunded);
        if (date > today)
            return Result.Failure(ContractErrors.InvalidRefundDate);
        if (TextNormalizer.TrimToNull(note) is not { } reason)
            return Result.Failure(ContractErrors.ForfeitReasonRequired);

        DepositStatus = DepositStatus.Forfeited;
        DepositRefundedOn = date;
        DepositRefundedAmount = 0;
        DepositNote = reason;
        return Result.Success();
    }

    public Result CancelDepositRefund()
    {
        if (DepositStatus == DepositStatus.Holding)
            return Result.Failure(ContractErrors.DepositNotRefunded);
        if (DepositStatus == DepositStatus.Deducted)
            return Result.Failure(ContractErrors.DepositDeducted);
        DepositStatus = DepositStatus.Holding;
        DepositRefundedOn = null;
        DepositRefundedAmount = null;
        DepositNote = null;
        return Result.Success();
    }

    /// <summary>M07 BL-BR-41: chốt hóa đơn trả phòng có dòng "Trừ tiền cọc" ⇒ cọc đã trừ hết vào hóa đơn đó (ngày = ngày chốt).</summary>
    public Result DeductDepositOnFinal(DateOnly date, string finalInvoiceNo)
    {
        if (!HasDeposit)
            return Result.Failure(ContractErrors.NoDeposit);
        if (DepositStatus == DepositStatus.Deducted)
            return Result.Failure(ContractErrors.DepositDeducted);
        if (DepositStatus != DepositStatus.Holding)
            return Result.Failure(ContractErrors.DepositAlreadyRefunded);

        DepositStatus = DepositStatus.Deducted;
        DepositRefundedOn = date;
        DepositRefundedAmount = DepositAmount;
        DepositNote = $"Trừ vào hóa đơn trả phòng {finalInvoiceNo}";
        return Result.Success();
    }

    /// <summary>Hủy hóa đơn trả phòng đã trừ cọc ⇒ cọc về đang giữ.</summary>
    public void UndoDepositDeduction()
    {
        if (DepositStatus != DepositStatus.Deducted)
            return;
        DepositStatus = DepositStatus.Holding;
        DepositRefundedOn = null;
        DepositRefundedAmount = null;
        DepositNote = null;
    }

    public void MarkDepositTransferred(string newContractNo)
    {
        if (!HasDeposit || DepositStatus != DepositStatus.Holding)
            return;
        DepositStatus = DepositStatus.Transferred;
        DepositNote = TransferredNote(newContractNo);
    }

    private static string TransferredNote(string newContractNo) => $"Chuyển sang hợp đồng {newContractNo}";
}

using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    // Cọc (chỉ theo dõi — M08 PM-BR-32..34)
    /// <summary>
    /// PM-BR-33: "Đã hoàn trả cọc" — ở mọi trạng thái HĐ. <paramref name="amount"/> null = đủ cọc; trả ít hơn (giữ lại một phần / mất cọc)
    /// ⇒ ghi chú bắt buộc.
    /// </summary>
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

    /// <summary>Bỏ đánh dấu hoàn trả / chuyển cọc (nhập nhầm) ⇒ về "Đang giữ cọc".</summary>
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

    /// <summary>PM-BR-34: ký lại — cọc của HĐ cũ chuyển sang HĐ mới (HĐ mới chép cọc thỏa thuận).</summary>
    public void MarkDepositTransferred(string newContractNo)
    {
        if (!HasDeposit || DepositStatus != DepositStatus.Holding)
            return;
        DepositStatus = DepositStatus.Transferred;
        DepositNote = TransferredNote(newContractNo);
    }

    /// <summary>
    /// PM-BR-34: ký lại không thành — HĐ nháp nối tiếp bị hủy (<paramref name="newContractNo"/> = số HĐ đó) hoặc HĐ này hủy thanh lý (null) ⇒ cọc về
    /// "Đang giữ" để còn nhắc hoàn cọc khi HĐ kết thúc.
    /// </summary>
    public void RevertDepositTransfer(string? newContractNo = null)
    {
        if (DepositStatus != DepositStatus.Transferred || (newContractNo is not null && DepositNote != TransferredNote(newContractNo)))
            return;
        DepositStatus = DepositStatus.Holding;
        DepositNote = null;
    }

    private static string TransferredNote(string newContractNo) => $"Chuyển sang hợp đồng {newContractNo}";
}

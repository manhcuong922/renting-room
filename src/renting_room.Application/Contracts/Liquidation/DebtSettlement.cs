namespace renting_room.Application.Contracts;

/// <summary>CT-BR-12: còn nợ khi hoàn tất — thu nhanh toàn bộ hoặc bỏ nợ.</summary>
public enum DebtSettlement
{
    /// <summary>"Đã thu toàn bộ": ghi 1 phiếu thu đúng tổng còn nợ.</summary>
    CollectAll,

    /// <summary>"Bỏ nợ": đóng công nợ không thu tiền — không tính doanh thu (PM-BR-16).</summary>
    WriteOff
}

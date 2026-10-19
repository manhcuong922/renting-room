namespace renting_room.Domain.Contracts;

/// <summary>M08 PM-BR-32: cọc chỉ để theo dõi — có nghĩa khi <c>deposit_amount</c> &gt; 0.</summary>
public enum DepositStatus
{
    Holding,

    /// <summary>Đã hoàn trả (ngày, số tiền thực trả — có thể ít hơn cọc, ghi chú).</summary>
    Refunded,

    /// <summary>Ký lại: cọc chuyển sang HĐ mới (PM-BR-34).</summary>
    Transferred
}

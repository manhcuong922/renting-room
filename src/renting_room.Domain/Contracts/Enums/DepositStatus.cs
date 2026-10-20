namespace renting_room.Domain.Contracts;

public enum DepositStatus
{
    Holding,

    Refunded,

    Transferred,

    /// <summary>Không hoàn cọc (mất cọc theo điều khoản) — PM-BR-33, lý do bắt buộc.</summary>
    Forfeited
}

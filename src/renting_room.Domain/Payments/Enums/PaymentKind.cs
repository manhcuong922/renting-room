using renting_room.Domain.Contracts;

namespace renting_room.Domain.Payments;

/// <summary><c>Receipt</c> = tiền thật; <c>WriteOff</c> = bỏ nợ (PM-BR-16) — đóng công nợ, không tính doanh thu.</summary>
public enum PaymentKind
{
    Receipt,
    WriteOff
}

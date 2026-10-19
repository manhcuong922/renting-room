using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

/// <summary>Đoạn đo của khoản chốt điện nước khi chuyển phòng (BL-BR-38).</summary>
public sealed record MeterClosing(Guid MeterId, Guid StartReadingId, Guid EndReadingId, Guid FeeTypeId, decimal Quantity, decimal UnitPrice);

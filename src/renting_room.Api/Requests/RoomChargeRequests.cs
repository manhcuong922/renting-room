using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Requests;

/// <param name="Settled">true ⇒ đã thanh toán (phụ thu) / đã hoàn (bù) ngay — cần <paramref name="Method"/>; <paramref name="SettledOn"/> bỏ trống = ngày phát sinh.</param>
public sealed record CreateRoomChargeRequest(
    RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn, string Reason, bool? Settled, DateOnly? SettledOn,
    PaymentMethod? Method);

public sealed record SettleRoomChargeRequest(DateOnly SettledOn, PaymentMethod Method);

public sealed record CancelRoomChargeRequest(string Reason);

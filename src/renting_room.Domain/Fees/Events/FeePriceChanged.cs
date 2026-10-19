using renting_room.Domain.Common;

namespace renting_room.Domain.Fees;

/// <summary>Bảng giá của khoản thu đổi từ ngày <paramref name="EffectiveFrom"/>.</summary>
public sealed record FeePriceChanged(Guid FeeTypeId, DateOnly EffectiveFrom) : IDomainEvent;

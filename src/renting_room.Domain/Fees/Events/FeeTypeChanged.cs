using renting_room.Domain.Common;

namespace renting_room.Domain.Fees;

/// <summary>Thông tin khoản thu đổi (tên, đơn vị, ngừng dùng…).</summary>
public sealed record FeeTypeChanged(Guid FeeTypeId) : IDomainEvent;

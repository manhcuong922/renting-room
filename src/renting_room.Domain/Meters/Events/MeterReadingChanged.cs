using renting_room.Domain.Common;

namespace renting_room.Domain.Meters;

/// <summary>Chỉ số của công tơ được ghi / sửa / hủy.</summary>
public sealed record MeterReadingChanged(Guid MeterId) : IDomainEvent;

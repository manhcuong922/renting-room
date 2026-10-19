using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Domain.Contracts;

/// <summary>Vi phạm của người ở thứ <see cref="Index"/> trong danh sách được kiểm tra; <see cref="Field"/> là tên field JSON.</summary>
public sealed record OccupantRuleViolation(int Index, string Field, Error Error);

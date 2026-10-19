using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Domain.Contracts;

/// <summary>Thông tin tối thiểu của một người để kiểm tra quan hệ.</summary>
public sealed record PersonFacts(Guid RenterId, DateOnly DateOfBirth, Gender Gender);

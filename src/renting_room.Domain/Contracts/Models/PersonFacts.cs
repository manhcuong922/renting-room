using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Domain.Contracts;

public sealed record PersonFacts(Guid RenterId, DateOnly DateOfBirth, Gender Gender);

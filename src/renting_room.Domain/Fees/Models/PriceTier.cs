using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

public sealed record PriceTier(decimal? UpTo, decimal Price);

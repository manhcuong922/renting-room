using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

/// <summary>Một bậc giá: áp cho phần sản lượng tới <see cref="UpTo"/> (lũy kế); bậc cuối <c>UpTo = null</c> (FE-BR-15).</summary>
public sealed record PriceTier(decimal? UpTo, decimal Price);

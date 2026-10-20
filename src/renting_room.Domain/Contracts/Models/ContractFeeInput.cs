namespace renting_room.Domain.Contracts;

public sealed record ContractFeeInput(Guid FeeTypeId, decimal Quantity, decimal? UnitPriceOverride);

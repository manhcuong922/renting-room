using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Endpoints;

public sealed record CreateFeeTypeRequest(
    string Name, FeeGroup Group, ChargeBasis? ChargeBasis, string Unit, bool AutoAttach, decimal? DefaultQuantity, int? SortOrder,
    FeePriceInput? InitialPrice, VehicleType? VehicleType = null);

public sealed record UpdateFeeTypeRequest(
    string Name, string Unit, bool AutoAttach, decimal? DefaultQuantity, int SortOrder, uint Version, VehicleType? VehicleType = null);

public sealed record CopyFeeCatalogRequest(bool? IncludePrices, DateOnly? EffectiveFrom);

public sealed record BulkFeeUsageRequest(FeeUsageAction Action, IReadOnlyList<Guid> ContractIds, decimal? Quantity, decimal? UnitPriceOverride);

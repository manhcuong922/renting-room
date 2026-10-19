namespace renting_room.Domain.Contracts;

/// <summary>Khoản thu gắn vào HĐ (CT-UC-06): số lượng (nhóm Quantity), giá riêng của HĐ (null = theo bảng giá của khu).</summary>
public sealed record ContractFeeInput(Guid FeeTypeId, decimal Quantity, decimal? UnitPriceOverride);

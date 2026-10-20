using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed class ContractFee : TenantEntity
{
    private ContractFee() { } // EF Core

    internal ContractFee(Guid contractId, Guid propertyId, ContractFeeInput input, DateOnly effectiveFrom)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        PropertyId = propertyId;
        FeeTypeId = input.FeeTypeId;
        Quantity = input.Quantity;
        UnitPriceOverride = input.UnitPriceOverride;
        EffectiveFrom = effectiveFrom;
    }

    public Guid ContractId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid FeeTypeId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? UnitPriceOverride { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    public bool Covers(DateOnly date) => EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    internal bool Replace(ContractFeeInput input)
    {
        if (Quantity == input.Quantity && UnitPriceOverride == input.UnitPriceOverride)
            return false;
        Quantity = input.Quantity;
        UnitPriceOverride = input.UnitPriceOverride;
        return true;
    }

    internal bool EndOn(DateOnly date)
    {
        if (EffectiveTo == date)
            return false;
        EffectiveTo = date;
        return true;
    }

    internal bool Reopen()
    {
        if (EffectiveTo is null)
            return false;
        EffectiveTo = null;
        return true;
    }
}

using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed class ContractRentTerm : TenantEntity
{
    private ContractRentTerm() { } // EF Core

    internal ContractRentTerm(Guid contractId, DateOnly effectiveFrom, decimal monthlyRent, string? addendumNo, string? note)
    {
        if (monthlyRent <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyRent));

        Id = Guid.CreateVersion7();
        ContractId = contractId;
        EffectiveFrom = effectiveFrom;
        MonthlyRent = monthlyRent;
        AddendumNo = TextNormalizer.TrimToNull(addendumNo);
        Note = TextNormalizer.TrimToNull(note);
    }

    public Guid ContractId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public decimal MonthlyRent { get; private set; }
    public string? AddendumNo { get; private set; }
    public string? Note { get; private set; }

    internal bool Replace(decimal monthlyRent, string? addendumNo, string? note)
    {
        if (monthlyRent <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyRent));
        var (rent, addendum, text) = (monthlyRent, TextNormalizer.TrimToNull(addendumNo), TextNormalizer.TrimToNull(note));
        if (MonthlyRent == rent && AddendumNo == addendum && Note == text)
            return false;
        (MonthlyRent, AddendumNo, Note) = (rent, addendum, text);
        return true;
    }
}

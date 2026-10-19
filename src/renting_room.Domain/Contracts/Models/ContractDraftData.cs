namespace renting_room.Domain.Contracts;

public sealed record ContractDraftData(
    Guid RepresentativeRenterId,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? SignedDate,
    string? SignedPlace,
    DateOnly? EffectiveDate,
    decimal MonthlyRent,
    decimal DepositAmount,
    string? DepositTerms,
    int NoticeDays,
    IReadOnlyCollection<PaymentMethod> PaymentMethods,
    int CopiesCount,
    string? TermsText,
    string? Note,
    IReadOnlyCollection<OccupantInput> Occupants,
    ContractDocument? Document = null,
    Guid? HouseholdHeadRenterId = null,
    IReadOnlyCollection<ContractFeeInput>? Fees = null,
    DateOnly? BillingStartDate = null);

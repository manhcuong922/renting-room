using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

public sealed class Property : TenantEntity
{
    private Property() { } // EF Core

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string StreetAddress { get; private set; } = null!;
    public string CommuneName { get; private set; } = null!;
    public string ProvinceName { get; private set; } = null!;
    public string? CommuneCode { get; private set; }
    public string? ProvinceCode { get; private set; }
    public string? Description { get; private set; }
    public string? EvnCustomerCode { get; private set; }

    public int BillingAnchorDay { get; private set; }
    public ChargeMode ChargeMode { get; private set; }
    public int PaymentDueDays { get; private set; }
    public ProrationMode ProrationMode { get; private set; }

    public bool RoundInvoiceTotal { get; private set; } = true;
    public int DefaultNoticeDays { get; private set; }
    public IReadOnlyList<BillingScheduleEntry> BillingScheduleEntries { get; private set; } = [];

    public LessorType? LessorType { get; private set; }
    public string? LessorName { get; private set; }
    public string? LessorAddress { get; private set; }
    public string? LessorPhone { get; private set; }
    public string? LessorEmail { get; private set; }
    public IdDocumentType? LessorIdType { get; private set; }
    public byte[]? LessorIdNumberEncrypted { get; private set; }
    public string? LessorIdNumberLast4 { get; private set; }
    public DateOnly? LessorIdIssueDate { get; private set; }
    public string? LessorIdIssuePlace { get; private set; }
    public DateOnly? LessorDateOfBirth { get; private set; }
    public string? LessorTaxCode { get; private set; }
    public string? LessorRepresentativeName { get; private set; }
    public string? LessorRepresentativeTitle { get; private set; }
    public string? AuthorizationDocNo { get; private set; }
    public DateOnly? AuthorizationDocDate { get; private set; }

    public string? LandParcelNo { get; private set; }
    public string? LandMapSheetNo { get; private set; }
    public string? OwnershipCertificateNo { get; private set; }

    public string? BankName { get; private set; }
    public string? BankAccountNo { get; private set; }
    public string? BankAccountName { get; private set; }
    public string? HouseRulesText { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public BillingSettings BillingSettings => new(BillingAnchorDay, ChargeMode, PaymentDueDays, ProrationMode, DefaultNoticeDays, RoundInvoiceTotal);

    public BillingSchedule BillingSchedule => new(BillingScheduleEntries);

    public PropertyAddress Address => new(StreetAddress, CommuneName, ProvinceName, CommuneCode, ProvinceCode);

    public LessorDetails? Lessor => LessorType is null
        ? null
        : new LessorDetails(
            LessorType.Value, LessorName!, LessorAddress!, LessorPhone!, LessorEmail, LessorIdType, LessorIdNumberEncrypted,
            LessorIdNumberLast4, LessorIdIssueDate, LessorIdIssuePlace, LessorDateOfBirth, LessorTaxCode,
            LessorRepresentativeName, LessorRepresentativeTitle, AuthorizationDocNo, AuthorizationDocDate);

    public BankAccount? BankAccount => BankAccountNo is null ? null : new BankAccount(BankName!, BankAccountNo, BankAccountName!);

    public static Property Create(string code, string name, PropertyAddress address, BillingSettings billing)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Property code is required.", nameof(code));

        var property = new Property { Id = Guid.CreateVersion7(), Code = TextNormalizer.NormalizeCode(code) };
        property.UpdateInfo(name, address, description: null, evnCustomerCode: null, new LandParcel(null, null, null));
        billing.EnsureValid();
        property.SetSchedule(BillingSchedule.Single(billing.AnchorDay, billing.ChargeMode));
        property.UpdateBillingTerms(billing.PaymentDueDays, billing.ProrationMode, billing.NoticeDays, billing.RoundInvoiceTotal);
        return property;
    }

    public void UpdateInfo(string name, PropertyAddress address, string? description, string? evnCustomerCode, LandParcel land)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Property name is required.", nameof(name));

        Name = name.Trim();
        StreetAddress = address.StreetAddress.Trim();
        CommuneName = address.CommuneName.Trim();
        ProvinceName = address.ProvinceName.Trim();
        CommuneCode = TextNormalizer.TrimToNull(address.CommuneCode);
        ProvinceCode = TextNormalizer.TrimToNull(address.ProvinceCode);
        Description = TextNormalizer.TrimToNull(description);
        EvnCustomerCode = TextNormalizer.TrimToNull(evnCustomerCode);
        LandParcelNo = TextNormalizer.TrimToNull(land.ParcelNo);
        LandMapSheetNo = TextNormalizer.TrimToNull(land.MapSheetNo);
        OwnershipCertificateNo = TextNormalizer.TrimToNull(land.OwnershipCertificateNo);
    }

    public void UpdateBillingTerms(int paymentDueDays, ProrationMode prorationMode, int noticeDays, bool roundInvoiceTotal)
    {
        new BillingSettings(BillingSchedule.MinAnchorDay, ChargeMode, paymentDueDays, prorationMode, noticeDays).EnsureValid();
        PaymentDueDays = paymentDueDays;
        ProrationMode = prorationMode;
        DefaultNoticeDays = noticeDays;
        RoundInvoiceTotal = roundInvoiceTotal;
    }

    public Result ChangeBillingCycle(int anchorDay, ChargeMode chargeMode, DateOnly? from, int? adjustDays)
    {
        if (anchorDay is < BillingSchedule.MinAnchorDay or > BillingSchedule.MaxAnchorDay)
            throw new ArgumentOutOfRangeException(nameof(anchorDay));
        if (from is not { } start)
        {
            SetSchedule(BillingSchedule.Single(anchorDay, chargeMode));
            return Result.Success();
        }

        var schedule = BillingSchedule;
        var previous = schedule.Entries.Last(e => e.EffectiveFrom < start);
        var deviation = BillingSchedule.TransitionDeviation(start, previous.AnchorDay, anchorDay);
        if (adjustDays is { } adjust && !BillingSchedule.IsAdjustAllowed(deviation, adjust))
            return Result.Failure(PropertyErrors.TransitionAdjustOutOfRange);
        SetSchedule(schedule.ChangeFrom(start, anchorDay, chargeMode, adjustDays));
        return Result.Success();
    }

    private void SetSchedule(BillingSchedule schedule)
    {
        BillingScheduleEntries = schedule.Entries.ToArray();
        BillingAnchorDay = schedule.Current.AnchorDay;
        ChargeMode = schedule.Current.ChargeMode;
    }

    public void UpdateLessor(LessorDetails lessor) => SetLessor(lessor.Normalized());

    public void ClearLessor() => SetLessor(null);

    private void SetLessor(LessorDetails? lessor)
    {
        LessorType = lessor?.Type;
        LessorName = lessor?.Name;
        LessorAddress = lessor?.Address;
        LessorPhone = lessor?.Phone;
        LessorEmail = lessor?.Email;
        LessorIdType = lessor?.IdType;
        LessorIdNumberEncrypted = lessor?.IdNumberEncrypted;
        LessorIdNumberLast4 = lessor?.IdNumberLast4;
        LessorIdIssueDate = lessor?.IdIssueDate;
        LessorIdIssuePlace = lessor?.IdIssuePlace;
        LessorDateOfBirth = lessor?.DateOfBirth;
        LessorTaxCode = lessor?.TaxCode;
        LessorRepresentativeName = lessor?.RepresentativeName;
        LessorRepresentativeTitle = lessor?.RepresentativeTitle;
        AuthorizationDocNo = lessor?.AuthorizationDocNo;
        AuthorizationDocDate = lessor?.AuthorizationDocDate;
    }

    public void UpdateBankAccount(BankAccount? account)
    {
        BankName = account?.BankName.Trim();
        BankAccountNo = account?.AccountNo.Trim();
        BankAccountName = account?.AccountName.Trim();
    }

    public void UpdateHouseRules(string? text) => HouseRulesText = TextNormalizer.TrimToNull(text);

    public Result Archive(DateTimeOffset now)
    {
        if (IsArchived)
            return Result.Failure(PropertyErrors.PropertyArchived);

        ArchivedAt = now;
        return Result.Success();
    }

    public Result Restore()
    {
        if (!IsArchived)
            return Result.Failure(PropertyErrors.PropertyNotArchived);

        ArchivedAt = null;
        return Result.Success();
    }
}

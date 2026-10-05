using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

/// <summary>Khu trọ — một địa điểm cho thuê có địa chỉ, bên cho thuê và cài đặt thu riêng.</summary>
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

    public int DefaultBillingAnchorDay { get; private set; }
    public ChargeMode DefaultChargeMode { get; private set; }
    public int DefaultPaymentDueDays { get; private set; }
    public ProrationMode DefaultProrationMode { get; private set; }
    public int DefaultNoticeDays { get; private set; }
    public int DefaultRentCycleMonths { get; private set; } = 1;

    // Bên cho thuê (PR-BR-12) — lưu phẳng thành cột lessor_*
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

    public BillingDefaults BillingDefaults =>
        new(DefaultBillingAnchorDay, DefaultChargeMode, DefaultPaymentDueDays, DefaultProrationMode, DefaultNoticeDays, DefaultRentCycleMonths);

    public PropertyAddress Address => new(StreetAddress, CommuneName, ProvinceName, CommuneCode, ProvinceCode);

    public LessorDetails? Lessor => LessorType is null
        ? null
        : new LessorDetails(
            LessorType.Value, LessorName!, LessorAddress!, LessorPhone!, LessorEmail, LessorIdType, LessorIdNumberEncrypted,
            LessorIdNumberLast4, LessorIdIssueDate, LessorIdIssuePlace, LessorDateOfBirth, LessorTaxCode,
            LessorRepresentativeName, LessorRepresentativeTitle, AuthorizationDocNo, AuthorizationDocDate);

    public BankAccount? BankAccount => BankAccountNo is null ? null : new BankAccount(BankName!, BankAccountNo, BankAccountName!);

    public static Property Create(string code, string name, PropertyAddress address, BillingDefaults billingDefaults)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Property code is required.", nameof(code));

        var property = new Property { Id = Guid.NewGuid(), Code = TextNormalizer.NormalizeCode(code) };
        property.UpdateInfo(name, address, description: null, evnCustomerCode: null, new LandParcel(null, null, null));
        property.UpdateBillingDefaults(billingDefaults);
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

    public void UpdateBillingDefaults(BillingDefaults defaults)
    {
        defaults.EnsureValid();
        DefaultBillingAnchorDay = defaults.AnchorDay;
        DefaultChargeMode = defaults.ChargeMode;
        DefaultPaymentDueDays = defaults.PaymentDueDays;
        DefaultProrationMode = defaults.ProrationMode;
        DefaultNoticeDays = defaults.NoticeDays;
        DefaultRentCycleMonths = defaults.RentCycleMonths;
    }

    /// <summary>Sửa bên cho thuê không ảnh hưởng hợp đồng đã kích hoạt — hợp đồng giữ snapshot (PR-BR-15).</summary>
    public void UpdateLessor(LessorDetails lessor)
    {
        LessorType = lessor.Type;
        LessorName = lessor.Name.Trim();
        LessorAddress = lessor.Address.Trim();
        LessorPhone = lessor.Phone.Trim();
        LessorEmail = TextNormalizer.TrimToNull(lessor.Email);
        LessorIdType = lessor.IdType;
        LessorIdNumberEncrypted = lessor.IdNumberEncrypted;
        LessorIdNumberLast4 = lessor.IdNumberLast4;
        LessorIdIssueDate = lessor.IdIssueDate;
        LessorIdIssuePlace = TextNormalizer.TrimToNull(lessor.IdIssuePlace);
        LessorDateOfBirth = lessor.DateOfBirth;
        LessorTaxCode = TextNormalizer.TrimToNull(lessor.TaxCode);
        LessorRepresentativeName = TextNormalizer.TrimToNull(lessor.RepresentativeName);
        LessorRepresentativeTitle = TextNormalizer.TrimToNull(lessor.RepresentativeTitle);
        AuthorizationDocNo = TextNormalizer.TrimToNull(lessor.AuthorizationDocNo);
        AuthorizationDocDate = lessor.AuthorizationDocDate;
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

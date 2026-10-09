using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

/// <summary>Thu tiền phòng đầu kỳ (điện nước kỳ trước) hay cuối kỳ.</summary>
public enum ChargeMode
{
    Prepaid,
    Postpaid
}

/// <summary>Kỳ lẻ tính theo ngày hay tính trọn 1 tháng.</summary>
public enum ProrationMode
{
    Daily,
    FullPeriod
}

public enum LessorType
{
    Individual,
    Organization
}

/// <summary>Cài đặt kỳ thu của khu — mọi HĐ của khu dùng chung (PR-BR-09); <c>NoticeDays</c> chỉ là gợi ý khi tạo HĐ.</summary>
public sealed record BillingSettings(int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays)
{
    /// <summary>Mặc định khu mới (chốt 09/10/2026): chốt ngày 1, <b>thu sau</b>, tính theo ngày ở kỳ lẻ, hạn 5 ngày, báo trước 30 ngày.</summary>
    public static readonly BillingSettings Standard = new(1, ChargeMode.Postpaid, 5, ProrationMode.Daily, 30);

    public void EnsureValid()
    {
        if (AnchorDay is < Billing.BillingSchedule.MinAnchorDay or > Billing.BillingSchedule.MaxAnchorDay)
            throw new ArgumentOutOfRangeException(nameof(AnchorDay));
        if (PaymentDueDays is < 0 or > 60)
            throw new ArgumentOutOfRangeException(nameof(PaymentDueDays));
        if (NoticeDays is < 0 or > 180)
            throw new ArgumentOutOfRangeException(nameof(NoticeDays));
    }
}

/// <summary>Địa chỉ theo mô hình 2 cấp (tỉnh – xã) từ 01/07/2025 (C-12). Mã ĐVHC tùy chọn cho tới khi seed bảng tham chiếu.</summary>
public sealed record PropertyAddress(
    string StreetAddress, string CommuneName, string ProvinceName, string? CommuneCode, string? ProvinceCode)
{
    public string FullText => $"{StreetAddress}, {CommuneName}, {ProvinceName}";
}

/// <summary>Thông tin bên cho thuê (PR-BR-12). Số giấy tờ đã được mã hóa ở tầng Application.</summary>
public sealed record LessorDetails(
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    byte[]? IdNumberEncrypted,
    string? IdNumberLast4,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate)
{
    public const int MinimumAge = 18;

    public LessorDetails Normalized() => this with
    {
        Name = Name.Trim(),
        Address = Address.Trim(),
        Phone = Phone.Trim(),
        Email = TextNormalizer.TrimToNull(Email),
        IdIssuePlace = TextNormalizer.TrimToNull(IdIssuePlace),
        TaxCode = TextNormalizer.TrimToNull(TaxCode),
        RepresentativeName = TextNormalizer.TrimToNull(RepresentativeName),
        RepresentativeTitle = TextNormalizer.TrimToNull(RepresentativeTitle),
        AuthorizationDocNo = TextNormalizer.TrimToNull(AuthorizationDocNo)
    };

    /// <summary>PR-BR-12: đủ thông tin bắt buộc để làm bên cho thuê trong hợp đồng.</summary>
    public bool IsComplete(DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Address) || string.IsNullOrWhiteSpace(Phone))
            return false;

        return Type switch
        {
            LessorType.Individual => IdType is not null && IdNumberEncrypted is { Length: > 0 }
                && DateOfBirth is { } dob && dob.AgeOn(today) >= MinimumAge,
            LessorType.Organization => !string.IsNullOrWhiteSpace(TaxCode) && !string.IsNullOrWhiteSpace(RepresentativeName)
                && !string.IsNullOrWhiteSpace(RepresentativeTitle),
            _ => false
        };
    }
}

public sealed record BankAccount(string BankName, string AccountNo, string AccountName);

public sealed record LandParcel(string? ParcelNo, string? MapSheetNo, string? OwnershipCertificateNo);

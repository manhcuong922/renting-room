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

/// <summary>Cài đặt thu mặc định của khu — chỉ là giá trị gợi ý khi tạo hợp đồng (PR-BR-09).</summary>
/// <param name="RentCycleMonths">Chu kỳ đóng tiền phòng mặc định (1/2/3/6/12 tháng — BL-BR-26).</param>
public sealed record BillingDefaults(
    int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays, int RentCycleMonths = 1)
{
    public static readonly int[] AllowedRentCycles = [1, 2, 3, 6, 12];

    public static readonly BillingDefaults Standard = new(1, ChargeMode.Prepaid, 5, ProrationMode.Daily, 30);

    public void EnsureValid()
    {
        if (AnchorDay is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(AnchorDay));
        if (PaymentDueDays is < 0 or > 60)
            throw new ArgumentOutOfRangeException(nameof(PaymentDueDays));
        if (NoticeDays is < 0 or > 180)
            throw new ArgumentOutOfRangeException(nameof(NoticeDays));
        if (!AllowedRentCycles.Contains(RentCycleMonths))
            throw new ArgumentOutOfRangeException(nameof(RentCycleMonths));
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

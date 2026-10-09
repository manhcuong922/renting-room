using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

public enum Gender
{
    Male,
    Female,
    Other
}

/// <summary>
/// Hồ sơ một người (người đại diện ký hợp đồng và/hoặc người ở). Số giấy tờ lưu MÃ HÓA + hash để chống trùng
/// (RT-BR-02); hiển thị chỉ 4 số cuối (LEG-06).
/// </summary>
public sealed class Renter : TenantEntity
{
    private Renter() { } // EF Core

    public string FullName { get; private set; } = null!;
    public string FullNameSearch { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public Gender Gender { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string Nationality { get; private set; } = "VN";
    /// <summary>RT-BR-01: null = chưa có giấy tờ (chỉ được khi dưới <see cref="IdRequiredAge"/> tuổi lúc khai).</summary>
    public IdDocumentType? IdType { get; private set; }
    public byte[]? IdNumberEncrypted { get; private set; }
    public string? IdNumberHash { get; private set; }
    public string? IdNumberLast4 { get; private set; }
    public DateOnly? IdIssueDate { get; private set; }
    public string? IdIssuePlace { get; private set; }
    public string? PermanentAddress { get; private set; }
    public string? Occupation { get; private set; }
    public string? Workplace { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>RT-BR-06: đã ẩn danh — dữ liệu cá nhân đã xóa, không sửa / không dùng cho HĐ mới được nữa.</summary>
    public DateTimeOffset? AnonymizedAt { get; private set; }

    public bool IsAnonymized => AnonymizedAt is not null;

    /// <summary>RT-BR-01: tuổi được cấp CCCD — từ tuổi này bắt buộc có số giấy tờ.</summary>
    public const int IdRequiredAge = 14;

    public bool HasIdNumber => IdNumberEncrypted is { Length: > 0 };

    public static Renter Create(RenterProfile profile, ProtectedIdNumber? idNumber)
    {
        var renter = new Renter { Id = Guid.CreateVersion7() };
        renter.Update(profile, idNumber);
        return renter;
    }

    public void Update(RenterProfile profile, ProtectedIdNumber? idNumber)
    {
        if (IsAnonymized)
            throw new InvalidOperationException("Anonymized renters cannot be updated.");
        if (string.IsNullOrWhiteSpace(profile.FullName))
            throw new ArgumentException("Full name is required.", nameof(profile));

        FullName = profile.FullName.Trim();
        FullNameSearch = TextNormalizer.ToSearchText(profile.FullName);
        DateOfBirth = profile.DateOfBirth;
        Gender = profile.Gender;
        Phone = profile.Phone;
        Email = TextNormalizer.TrimToNull(profile.Email)?.ToLowerInvariant();
        Nationality = profile.Nationality.Trim().ToUpperInvariant();
        IdType = idNumber?.Type;
        IdNumberEncrypted = idNumber?.Encrypted;
        IdNumberHash = idNumber?.Hash;
        IdNumberLast4 = idNumber?.Last4;
        IdIssueDate = profile.IdIssueDate;
        IdIssuePlace = TextNormalizer.TrimToNull(profile.IdIssuePlace);
        PermanentAddress = TextNormalizer.TrimToNull(profile.PermanentAddress);
        Occupation = TextNormalizer.TrimToNull(profile.Occupation);
        Workplace = TextNormalizer.TrimToNull(profile.Workplace);
        EmergencyContactName = TextNormalizer.TrimToNull(profile.EmergencyContactName);
        EmergencyContactPhone = TextNormalizer.TrimToNull(profile.EmergencyContactPhone);
        Note = TextNormalizer.TrimToNull(profile.Note);
    }

    public int AgeOn(DateOnly date) => DateOfBirth.AgeOn(date);

    /// <summary>Tên thay thế sau khi ẩn danh — 4 ký tự cuối của id để chủ trọ còn phân biệt các hồ sơ đã ẩn danh.</summary>
    public static string AnonymizedName(Guid renterId) => $"Đã ẩn danh #{renterId.ToString("N")[^4..].ToUpperInvariant()}";

    /// <summary>
    /// RT-BR-06: xóa mọi dữ liệu nhận diện (họ tên, ngày sinh, giấy tờ, liên hệ, địa chỉ, nghề nghiệp, ghi chú); giữ id, giới tính, quốc tịch
    /// để liên kết tài chính và thống kê còn nguyên. Xóa cả giấy tờ ⇒ cùng CCCD đăng ký lại là hồ sơ mới.
    /// Không đảo ngược.
    /// </summary>
    public void Anonymize(DateTimeOffset now)
    {
        if (IsAnonymized)
            return;

        FullName = AnonymizedName(Id);
        FullNameSearch = TextNormalizer.ToSearchText(FullName);
        DateOfBirth = DateOnly.MinValue;
        Phone = Email = IdIssuePlace = PermanentAddress = Occupation = Workplace = EmergencyContactName = EmergencyContactPhone = Note = null;
        IdIssueDate = null;
        IdType = null;
        IdNumberEncrypted = null;
        IdNumberHash = null;
        IdNumberLast4 = null;
        ArchivedAt ??= now;
        AnonymizedAt = now;
    }
}

public sealed record RenterProfile(
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? Phone,
    string? Email,
    string Nationality,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    string? PermanentAddress,
    string? Occupation,
    string? Workplace,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Note);

/// <summary>Số giấy tờ đã chuẩn hóa → mã hóa (để đọc lại) + hash (để tìm/chống trùng) + 4 số cuối (để hiển thị).</summary>
public sealed record ProtectedIdNumber(IdDocumentType Type, byte[] Encrypted, string Hash, string Last4);

public static class RenterErrors
{
    public static readonly Error RenterNotFound = Error.NotFound("RENTER_NOT_FOUND", "Không tìm thấy người thuê.");
    public static readonly Error Anonymized = Error.BusinessRule("RENTER_ANONYMIZED",
        "Hồ sơ đã ẩn danh — không còn dữ liệu cá nhân; người này thuê lại thì tạo hồ sơ mới.");
    public static readonly Error HasOpenContract = Error.BusinessRule("RENTER_HAS_ACTIVE_CONTRACT",
        "Người này còn hợp đồng nháp / đang hiệu lực / đang thanh lý — kết thúc hoặc hủy trước khi ẩn danh.");
    public static readonly Error HasUnsettledInvoices = Error.BusinessRule("RENTER_HAS_UNSETTLED_INVOICES",
        "Hợp đồng người này đứng tên còn phiếu chưa thu đủ hoặc chờ hoàn tiền — xử lý xong trước khi ẩn danh.");
    public static readonly Error IdNumberExists = Error.Conflict("RENTER_ID_NUMBER_EXISTS",
        "Số giấy tờ đã có trong hệ thống — hãy dùng lại hồ sơ cũ (một người được đứng tên nhiều phòng).");

    /// <summary>RT-BR-02: kèm <c>existingRenterId</c> để UI mở / chọn hồ sơ cũ.</summary>
    public static readonly Error IdNumberRequired = Error.Validation("ID_NUMBER_REQUIRED", "Từ 14 tuổi phải có số giấy tờ.");

    public static Error IdNumberExistsFor(Guid existingRenterId) => IdNumberExists.WithDetail("existingRenterId", existingRenterId);
}

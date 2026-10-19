using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

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
    /// <summary>RT-BR-05: ẩn hồ sơ khỏi danh sách / tìm kiếm mặc định (không xóa — HĐ, phiếu, lịch sử phòng vẫn nguyên).</summary>
    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    /// <summary>Hiện lại hồ sơ đã ẩn. Hồ sơ đã ẩn danh thì giữ ẩn.</summary>
    public void Restore()
    {
        if (!IsAnonymized)
            ArchivedAt = null;
    }

    public static string AnonymizedName(Guid renterId) => $"Đã ẩn danh #{renterId.ToString("N")[^4..].ToUpperInvariant()}";

    /// <summary>
    /// RT-BR-06: xóa mọi dữ liệu nhận diện (họ tên, ngày sinh, giấy tờ, liên hệ, địa chỉ, nghề nghiệp, ghi chú); giữ id, giới tính, quốc tịch
    /// để liên kết tài chính và thống kê còn nguyên. Xóa cả giấy tờ ⇒ cùng CCCD đăng ký lại là hồ sơ mới.
    /// Không đảo ngược.
    /// </summary>
    /// <param name="keepName">Hết thời gian giữ (tự động): giữ họ tên để còn tra lịch sử phòng; theo yêu cầu của người thuê: xóa cả tên.</param>
    public void Anonymize(DateTimeOffset now, bool keepName = false)
    {
        if (IsAnonymized)
            return;

        if (!keepName)
        {
            FullName = AnonymizedName(Id);
            FullNameSearch = TextNormalizer.ToSearchText(FullName);
        }
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

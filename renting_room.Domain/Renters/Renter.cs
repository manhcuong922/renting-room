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
    public IdDocumentType IdType { get; private set; }
    public byte[] IdNumberEncrypted { get; private set; } = null!;
    public string IdNumberHash { get; private set; } = null!;
    public string IdNumberLast4 { get; private set; } = null!;
    public DateOnly? IdIssueDate { get; private set; }
    public string? IdIssuePlace { get; private set; }
    public string? PermanentAddress { get; private set; }
    public string? Occupation { get; private set; }
    public string? Workplace { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public static Renter Create(RenterProfile profile, ProtectedIdNumber idNumber)
    {
        var renter = new Renter { Id = Guid.NewGuid() };
        renter.Update(profile, idNumber);
        return renter;
    }

    public void Update(RenterProfile profile, ProtectedIdNumber idNumber)
    {
        if (string.IsNullOrWhiteSpace(profile.FullName))
            throw new ArgumentException("Full name is required.", nameof(profile));

        FullName = profile.FullName.Trim();
        FullNameSearch = TextNormalizer.ToSearchText(profile.FullName);
        DateOfBirth = profile.DateOfBirth;
        Gender = profile.Gender;
        Phone = profile.Phone;
        Email = TextNormalizer.TrimToNull(profile.Email)?.ToLowerInvariant();
        Nationality = profile.Nationality.Trim().ToUpperInvariant();
        IdType = idNumber.Type;
        IdNumberEncrypted = idNumber.Encrypted;
        IdNumberHash = idNumber.Hash;
        IdNumberLast4 = idNumber.Last4;
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
    public static readonly Error IdNumberExists = Error.Conflict("RENTER_ID_NUMBER_EXISTS",
        "Số giấy tờ đã có trong hệ thống — hãy dùng lại hồ sơ cũ.");
}

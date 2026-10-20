using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

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

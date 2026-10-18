using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Imports;

// RT-UC-10: import người thuê đang ở — chỉ tạo mới khi chuyển từ sổ sang phần mềm
public enum ImportRole
{
    /// <summary>Đứng tên HĐ và ở trong phòng.</summary>
    Representative,
    /// <summary>Đứng tên nhưng không ở (VD bố mẹ thuê cho con) — một người được đứng tên nhiều phòng.</summary>
    RepresentativeNotLiving,
    /// <summary>Ở cùng người đứng tên.</summary>
    Occupant
}

/// <param name="MonthlyRent">Chỉ đọc ở dòng đứng tên.</param>
public sealed record TenancyImportRow(
    int RowNumber,
    string? RoomCode,
    ImportRole? Role,
    string? FullName,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? Phone,
    IdDocumentType? IdType,
    string? IdNumber,
    string? Nationality,
    string? PermanentAddress,
    string? Occupation,
    OccupantRelationship? Relationship,
    DateOnly? MoveInDate,
    decimal? MonthlyRent,
    decimal? DepositAmount);

internal static class TenancyImportTemplate
{
    public const string Sheet = "Người thuê";

    public static readonly ImportColumn[] Columns =
    [
        new("room", "Mã phòng*", SpreadsheetColumnType.Text, 10),
        new("role", "Vai trò*", SpreadsheetColumnType.Text, 18),
        new("name", "Họ tên*", SpreadsheetColumnType.Text, 24),
        new("dob", "Ngày sinh*", SpreadsheetColumnType.Date, 12),
        new("gender", "Giới tính*", SpreadsheetColumnType.Text, 10),
        new("phone", "SĐT", SpreadsheetColumnType.Text, 13),
        new("idType", "Loại giấy tờ", SpreadsheetColumnType.Text, 12),
        new("idNumber", "Số giấy tờ", SpreadsheetColumnType.Text, 16),
        new("nationality", "Quốc tịch", SpreadsheetColumnType.Text, 10),
        new("address", "Quê quán / thường trú", SpreadsheetColumnType.Text, 28),
        new("occupation", "Nghề nghiệp", SpreadsheetColumnType.Text, 16),
        new("relationship", "Quan hệ với người đứng tên", SpreadsheetColumnType.Text, 18),
        new("moveIn", "Ngày vào ở*", SpreadsheetColumnType.Date, 12),
        new("rent", "Giá thuê", SpreadsheetColumnType.Money, 13),
        new("deposit", "Tiền cọc", SpreadsheetColumnType.Money, 13)
    ];

    public static readonly Dictionary<string, string> Headers = Columns.ToDictionary(c => c.Key, c => c.Header.TrimEnd('*'));

    public static readonly Dictionary<string, string> RenterPropertyToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fullName"] = "name", ["dateOfBirth"] = "dob", ["gender"] = "gender", ["phone"] = "phone", ["idType"] = "idType",
        ["idNumber"] = "idNumber", ["nationality"] = "nationality", ["permanentAddress"] = "address", ["occupation"] = "occupation"
    };

    private static readonly Dictionary<string, OccupantRelationship> Relationships = BuildRelationships();

    public static TenancyImportRow Parse(ImportRow row)
    {
        var role = Choice(row, "role", ParseRole, "Vai trò: Đứng tên / Đứng tên (không ở) / Ở cùng.");
        var gender = Choice(row, "gender", ParseGender, "Giới tính: Nam / Nữ / Khác.");
        var idType = Choice(row, "idType", ParseIdType, "Loại giấy tờ: CCCD / CMND / Hộ chiếu.");
        var relationship = Choice(row, "relationship", ParseRelationship, "Quan hệ không đúng danh mục (VD Vợ, Chồng, Con, Cùng ở thuê).");
        return new TenancyImportRow(row.RowNumber, row.Text("room") is { } room ? TextNormalizer.NormalizeCode(room) : null, role,
            row.Text("name"), row.Date("dob"), gender, row.Text("phone"), idType, row.Text("idNumber"), row.Text("nationality"),
            row.Text("address"), row.Text("occupation"), relationship, row.Date("moveIn"), row.Decimal("rent"), row.Decimal("deposit"));
    }

    private static T? Choice<T>(ImportRow row, string key, Func<string, T?> parse, string message) where T : struct
    {
        if (row.Text(key) is not { } text)
            return null;
        var value = parse(text);
        if (value is null)
            row.Error(key, "INVALID_FORMAT", message);
        return value;
    }

    public static ImportRole? ParseRole(string text) => text.Trim().ToLowerInvariant() switch
    {
        "đứng tên" or "dung ten" or "representative" => ImportRole.Representative,
        "đứng tên (không ở)" or "đứng tên không ở" or "dung ten (khong o)" or "representativenotliving" => ImportRole.RepresentativeNotLiving,
        "ở cùng" or "o cung" or "occupant" => ImportRole.Occupant,
        _ => null
    };

    public static Gender? ParseGender(string text) => text.Trim().ToLowerInvariant() switch
    {
        "nam" or "male" => Domain.Renters.Gender.Male,
        "nữ" or "nu" or "female" => Domain.Renters.Gender.Female,
        "khác" or "khac" or "other" => Domain.Renters.Gender.Other,
        _ => null
    };

    public static IdDocumentType? ParseIdType(string text) => text.Trim().ToLowerInvariant() switch
    {
        "cccd" or "căn cước" or "citizenid" => IdDocumentType.CitizenId,
        "cmnd" or "legacyid" => IdDocumentType.LegacyId,
        "hộ chiếu" or "ho chieu" or "passport" => IdDocumentType.Passport,
        _ => null
    };

    public static OccupantRelationship? ParseRelationship(string text) =>
        Relationships.TryGetValue(text.Trim().ToLowerInvariant(), out var relationship) ? relationship : null;

    private static Dictionary<string, OccupantRelationship> BuildRelationships()
    {
        var map = new Dictionary<string, OccupantRelationship>();
        foreach (var r in Enum.GetValues<OccupantRelationship>())
        {
            map[r.ToString().ToLowerInvariant()] = r;
            map[OccupantRelationshipLabels.Label(r).ToLowerInvariant()] = r;
        }
        foreach (var (alias, r) in new[]
                 {
                     ("con", OccupantRelationship.Child), ("bố", OccupantRelationship.Father), ("cha", OccupantRelationship.Father),
                     ("mẹ", OccupantRelationship.Mother), ("anh", OccupantRelationship.Sibling), ("chị", OccupantRelationship.Sibling),
                     ("em", OccupantRelationship.Sibling), ("bạn", OccupantRelationship.CoTenant), ("ở ghép", OccupantRelationship.CoTenant),
                     ("cháu", OccupantRelationship.NephewNiece), ("ông", OccupantRelationship.Grandparent), ("bà", OccupantRelationship.Grandparent)
                 })
            map.TryAdd(alias, r);
        return map;
    }

    public static bool IsRepresentative(ImportRole? role) => role is ImportRole.Representative or ImportRole.RepresentativeNotLiving;

    public static bool IsLiving(ImportRole? role) => role is ImportRole.Representative or ImportRole.Occupant;

    /// <summary>Khóa nhận diện người trong file: loại + số giấy tờ đã chuẩn hóa (người không giấy tờ ⇒ null — không gộp).</summary>
    public static string? PersonKey(TenancyImportRow row) =>
        row is { IdType: { } type, IdNumber: { } number } ? $"{type}:{IdDocumentNumber.Normalize(number)}" : null;

    public static RenterInput Renter(TenancyImportRow row) => new(row.FullName ?? string.Empty, row.DateOfBirth ?? default,
        row.Gender ?? Domain.Renters.Gender.Other, row.Phone, null, row.Nationality, row.IdType, row.IdNumber, null, null, row.PermanentAddress,
        row.Occupation, null, null, null, "Nhập từ Excel");
}

using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Exports;
using renting_room.Application.Meters;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Imports;

// ============================================================ RT-UC-10: import người thuê đang ở — chỉ tạo mới khi chuyển từ sổ sang phần mềm

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

/// <param name="Errors">Lỗi của cả phòng (phòng không có / đang có HĐ, thiếu người đứng tên…); lỗi từng người nằm ở dòng.</param>
public sealed record TenancyRoomDto(
    string RoomCode, IReadOnlyList<int> RowNumbers, bool IsValid, DateOnly? StartDate, DateOnly? BillingStartDate,
    IReadOnlyList<ImportIssue> Errors, IReadOnlyList<ImportIssue> Warnings);

public sealed record TenancyImportPreviewDto(
    int ValidRooms, int InvalidRooms, IReadOnlyList<ImportRowResult<TenancyImportRow>> Rows, IReadOnlyList<TenancyRoomDto> Rooms,
    IReadOnlyList<ImportIssue> FileErrors);

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

/// <summary>Kế hoạch tạo 1 lượt thuê — chỉ có khi cả phòng hợp lệ.</summary>
internal sealed record TenancyPlan(
    string RoomCode, Guid RoomId, IReadOnlyList<TenancyImportRow> Rows, DateOnly StartDate, DateOnly BillingStartDate, decimal MonthlyRent,
    decimal DepositAmount);

internal sealed record TenancyCheck(
    IReadOnlyList<ImportRowResult<TenancyImportRow>> Rows, IReadOnlyList<TenancyRoomDto> Rooms, IReadOnlyList<TenancyPlan> Plans);

/// <summary>Kiểm các dòng theo dữ liệu hiện tại — dùng chung cho xem trước và lưu (RT-UC-10).</summary>
internal sealed class TenancyImportChecker(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, IValidator<CreateRenterCommand> renterValidator)
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];
    private const int StaleReadingDays = 31;

    public async Task<TenancyCheck> CheckAsync(
        Property property, IReadOnlyList<TenancyImportRow> rows, IReadOnlyDictionary<int, List<ImportIssue>> parseErrors, DateOnly today,
        CancellationToken ct)
    {
        var errors = rows.ToDictionary(r => r.RowNumber, r => new List<ImportIssue>(parseErrors.GetValueOrDefault(r.RowNumber) ?? []));
        var warnings = rows.ToDictionary(r => r.RowNumber, _ => new List<ImportIssue>());
        void Error(TenancyImportRow r, string key, string code, string message) => errors[r.RowNumber].Add(new(code, message, TenancyImportTemplate.Headers[key]));
        void Warn(TenancyImportRow r, string? key, string code, string message) =>
            warnings[r.RowNumber].Add(new(code, message, key is null ? null : TenancyImportTemplate.Headers[key]));

        // 1. Từng dòng.
        foreach (var row in rows)
        {
            if (row.RoomCode is null)
                Error(row, "room", "REQUIRED", "Mã phòng là bắt buộc.");
            if (row.Role is null && !errors[row.RowNumber].Any(e => e.Column == TenancyImportTemplate.Headers["role"]))
                Error(row, "role", "REQUIRED", "Vai trò là bắt buộc.");
            var renterCheck = await renterValidator.ValidateAsync(new CreateRenterCommand(TenancyImportTemplate.Renter(row)), ct);
            errors[row.RowNumber].AddRange(ImportIssues.From(renterCheck, TenancyImportTemplate.RenterPropertyToKey, TenancyImportTemplate.Headers));
            if (TenancyImportTemplate.IsRepresentative(row.Role) && row.IdNumber is null)
                Error(row, "idNumber", "REPRESENTATIVE_ID_REQUIRED", "Người đứng tên phải có số giấy tờ.");
            if (row.MoveInDate is null)
                Error(row, "moveIn", "REQUIRED", "Ngày vào ở là bắt buộc.");
            else if (row.MoveInDate > today)
                Error(row, "moveIn", "START_DATE_IN_FUTURE", "Chỉ nhập người đang ở (ngày vào ở ≤ hôm nay) — người sắp vào thì tạo HĐ nháp ở màn HĐ.");
            else if (row.MoveInDate < today.AddYears(-10))
                Error(row, "moveIn", "INVALID_START_DATE", "Ngày vào ở không được trước hôm nay quá 10 năm.");
            if (TenancyImportTemplate.IsRepresentative(row.Role))
            {
                if (row.MonthlyRent is not > 0)
                    Error(row, "rent", "REQUIRED", "Dòng đứng tên phải có giá thuê > 0.");
                else if (row.MonthlyRent > 1_000_000_000)
                    Error(row, "rent", "OUT_OF_RANGE", "Giá thuê quá lớn.");
                if (row.DepositAmount < 0)
                    Error(row, "deposit", "OUT_OF_RANGE", "Tiền cọc không được âm.");
                else if (row.DepositAmount > row.MonthlyRent * ContractDraftBuilder.MaxDepositMonths)
                    Error(row, "deposit", "DEPOSIT_TOO_HIGH", $"Tiền cọc lớn hơn {ContractDraftBuilder.MaxDepositMonths} tháng tiền thuê.");
            }
            else if (row.MonthlyRent is not null || row.DepositAmount is not null)
                Warn(row, "rent", "IGNORED_FOR_OCCUPANT", "Giá thuê / tiền cọc chỉ đọc ở dòng đứng tên — dòng này bị bỏ qua.");
            if (row.Role == ImportRole.Occupant && row.Relationship is null)
                Warn(row, "relationship", "RELATIONSHIP_REQUIRED", "Chưa khai quan hệ với người đứng tên (khai sau trên HĐ được).");
        }

        // 2. Cùng một người trong file (theo số giấy tờ).
        foreach (var person in rows.Where(r => TenancyImportTemplate.PersonKey(r) is not null).GroupBy(r => TenancyImportTemplate.PersonKey(r)!))
        {
            var list = person.ToList();
            if (list.Select(r => TextNormalizer.ToSearchText(r.FullName ?? "")).Distinct().Count() > 1 || list.Select(r => r.DateOfBirth).Distinct().Count() > 1)
                list.ForEach(r => Error(r, "idNumber", "PERSON_MISMATCH", "Cùng số giấy tờ nhưng khác họ tên / ngày sinh ở các dòng."));
            if (list.Count(r => TenancyImportTemplate.IsLiving(r.Role)) > 1)
                list.Where(r => TenancyImportTemplate.IsLiving(r.Role)).ToList()
                    .ForEach(r => Error(r, "idNumber", "OCCUPANT_LIVES_ELSEWHERE",
                        "Một người chỉ ở 1 phòng — người thuê nhiều phòng dùng vai trò \"Đứng tên (không ở)\" ở các phòng còn lại."));
            foreach (var sameRoom in list.GroupBy(r => r.RoomCode).Where(g => g.Count() > 1))
                sameRoom.ToList().ForEach(r => Error(r, "idNumber", "DUPLICATE_IN_ROOM", "Một người xuất hiện 2 lần trong cùng phòng."));
        }

        // 3. So với dữ liệu đang có: hồ sơ cùng số giấy tờ, người đang ở HĐ khác.
        var organizationId = currentUser.OrganizationId!.Value;
        var hashes = rows.Where(r => r is { IdType: not null, IdNumber: not null })
            .ToDictionary(r => r.RowNumber, r => protector.ProtectIdNumber(organizationId, r.IdType!.Value, r.IdNumber!).Hash);
        var hashList = hashes.Values.Distinct().Select(h => (string?)h).ToList();
        var existing = await db.Renters.AsNoTracking().Where(r => hashList.Contains(r.IdNumberHash))
            .Select(r => new { r.Id, r.IdNumberHash, r.FullName, r.DateOfBirth, r.Phone }).ToListAsync(ct);
        var existingIds = existing.Select(e => e.Id).ToList();
        var stayingElsewhere = (await db.Contracts.AsNoTracking()
                .Where(c => OpenStatuses.Contains(c.Status))
                .SelectMany(c => c.Occupants.Where(o => existingIds.Contains(o.RenterId) && (o.MoveOutDate == null || o.MoveOutDate >= today))
                    .Select(o => new { o.RenterId, c.ContractNo, RoomCode = db.Rooms.Where(r => r.Id == c.RoomId).Select(r => r.Code).First() }))
                .ToListAsync(ct))
            .GroupBy(x => x.RenterId).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in rows.Where(r => hashes.ContainsKey(r.RowNumber)))
        {
            var match = existing.FirstOrDefault(e => e.IdNumberHash == hashes[row.RowNumber]);
            if (match is null)
                continue;
            Warn(row, "idNumber", "RENTER_EXISTS", "Đã có hồ sơ cùng số giấy tờ — dùng lại hồ sơ đó, không sửa.");
            if (TextNormalizer.ToSearchText(match.FullName) != TextNormalizer.ToSearchText(row.FullName ?? "") || match.DateOfBirth != row.DateOfBirth
                || (row.Phone is not null && RenterProjection.NormalizePhone(row.Phone) != match.Phone))
                Warn(row, "name", "RENTER_DATA_DIFFERS",
                    $"Thông tin trong file khác hồ sơ đang có ({match.FullName}, {match.DateOfBirth:dd/MM/yyyy}, {match.Phone ?? "không SĐT"}) — giữ hồ sơ, sửa ở màn người thuê nếu cần.");
            if (TenancyImportTemplate.IsLiving(row.Role) && stayingElsewhere.TryGetValue(match.Id, out var stay))
                Error(row, "idNumber", "OCCUPANT_LIVES_ELSEWHERE", $"Người này đang ở phòng {stay.RoomCode} (HĐ {stay.ContractNo}) — thanh lý / chuyển phòng ở màn HĐ trước.");
        }

        // 4. Theo phòng.
        var periodStart = property.BillingSchedule.StandardPeriodContaining(today).Start;
        var roomCodes = rows.Where(r => r.RoomCode is not null).Select(r => r.RoomCode!).Distinct().ToList();
        var roomsByCode = await db.Rooms.AsNoTracking().Where(r => r.PropertyId == property.Id && roomCodes.Contains(r.Code))
            .Select(r => new { r.Id, r.Code, r.ArchivedAt }).ToDictionaryAsync(r => r.Code, ct);
        var roomIds = roomsByCode.Values.Select(r => r.Id).ToList();
        var openContracts = (await db.Contracts.AsNoTracking().Where(c => roomIds.Contains(c.RoomId) && OpenStatuses.Contains(c.Status))
                .Select(c => new { c.RoomId, c.ContractNo, c.Status, Name = db.Renters.Where(r => r.Id == c.RepresentativeRenterId).Select(r => r.FullName).First() })
                .ToListAsync(ct))
            .GroupBy(c => c.RoomId).ToDictionary(g => g.Key, g => g.First());
        var lastReadings = (await db.Meters.AsNoTracking().Where(m => roomIds.Contains(m.RoomId) && m.RemovedDate == null)
                .Select(m => new { m.RoomId, m.SerialNo, Last = m.Readings.Max(r => (DateOnly?)r.ReadingDate) }).ToListAsync(ct))
            .ToLookup(m => m.RoomId);

        var rooms = new List<TenancyRoomDto>();
        var plans = new List<TenancyPlan>();
        foreach (var group in rows.Where(r => r.RoomCode is not null).GroupBy(r => r.RoomCode!))
        {
            var members = group.ToList();
            var roomErrors = new List<ImportIssue>();
            var roomWarnings = new List<ImportIssue>();
            var room = roomsByCode.GetValueOrDefault(group.Key);
            if (room is null || room.ArchivedAt is not null)
                roomErrors.Add(new("ROOM_NOT_FOUND", $"Khu không có phòng {group.Key} (hoặc đã ngừng dùng) — import phòng trước."));
            else if (openContracts.TryGetValue(room.Id, out var open))
                roomErrors.Add(new("ROOM_HAS_CONTRACTS",
                    $"Phòng {group.Key} đang có HĐ {open.ContractNo} của {open.Name} ({open.Status}) — import chỉ cho phòng trống; thêm người / ký lại / thanh lý ở màn HĐ."));
            var representatives = members.Where(r => TenancyImportTemplate.IsRepresentative(r.Role)).ToList();
            if (representatives.Count != 1)
                roomErrors.Add(new("REPRESENTATIVE_COUNT", representatives.Count == 0 ? "Phòng chưa có người đứng tên." : "Phòng có nhiều hơn 1 người đứng tên."));
            var living = members.Where(r => TenancyImportTemplate.IsLiving(r.Role)).ToList();
            if (living.Count == 0)
                roomErrors.Add(new("NO_OCCUPANT", "Phòng không có ai ở (vai trò \"Đứng tên\" hoặc \"Ở cùng\")."));

            DateOnly? start = living.Select(r => r.MoveInDate).Where(d => d is not null).Min();
            DateOnly? billingStart = start is { } s ? (s > periodStart ? s : periodStart) : null;
            if (room is not null && billingStart is { } from)
                foreach (var meter in lastReadings[room.Id].Where(m => m.Last is null || m.Last < from.AddDays(-StaleReadingDays)))
                    roomWarnings.Add(new("STALE_METER_READING",
                        $"Chỉ số công tơ {meter.SerialNo ?? ""} ghi lần cuối {meter.Last:dd/MM/yyyy} — cập nhật chỉ số trước, nếu không phiếu đầu sẽ gộp điện nước nhiều tháng."));

            var isValid = roomErrors.Count == 0 && members.All(r => errors[r.RowNumber].Count == 0);
            rooms.Add(new TenancyRoomDto(group.Key, members.Select(r => r.RowNumber).ToList(), isValid, start, billingStart, roomErrors, roomWarnings));
            if (isValid)
            {
                var representative = representatives[0];
                plans.Add(new TenancyPlan(group.Key, room!.Id, members, start!.Value, billingStart!.Value, representative.MonthlyRent!.Value,
                    representative.DepositAmount ?? 0));
            }
        }

        var results = rows.Select(r => new ImportRowResult<TenancyImportRow>(r, errors[r.RowNumber], warnings[r.RowNumber])).ToList();
        return new TenancyCheck(results, rooms, plans);
    }
}

public sealed record GetTenancyImportTemplateQuery(Guid PropertyId) : IRequest<Result<ExportFile>>;

public sealed class GetTenancyImportTemplateHandler(IAppDbContext db, ISpreadsheetWriter writer)
    : IRequestHandler<GetTenancyImportTemplateQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(GetTenancyImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var code = await db.Properties.Where(p => p.Id == request.PropertyId).Select(p => p.Code).FirstOrDefaultAsync(cancellationToken);
        if (code is null)
            return PropertyErrors.PropertyNotFound;

        var sheet = new SpreadsheetSheet(TenancyImportTemplate.Sheet, $"Người thuê đang ở — khu {code}",
            "Mỗi dòng 1 người; các dòng cùng mã phòng = 1 hợp đồng. Vai trò: Đứng tên / Đứng tên (không ở) / Ở cùng — mỗi phòng đúng 1 người đứng tên. " +
            "Giấy tờ bắt buộc từ 14 tuổi và với người đứng tên. Giá thuê, cọc chỉ ghi ở dòng đứng tên. Chỉ nhập phòng đang trống; dịch vụ, xe thêm sau.",
            TenancyImportTemplate.Columns.Select(c => new SpreadsheetColumn(c.Header, c.Type, c.Width)).ToList(),
            [
                ["101", "Đứng tên", "Phạm Văn Hùng", new DateOnly(1985, 4, 2), "Nam", "0912345678", "CCCD", "001085012345", "VN", "Nam Định", "Kỹ sư", null,
                    new DateOnly(2024, 3, 1), 3_500_000, 3_500_000],
                ["101", "Ở cùng", "Lê Thị Mai", new DateOnly(1988, 9, 20), "Nữ", null, "CCCD", "001188012346", "VN", "Nam Định", null, "Vợ",
                    new DateOnly(2024, 3, 1), null, null],
                ["101", "Ở cùng", "Phạm Minh An", new DateOnly(2018, 6, 1), "Nam", null, null, null, "VN", null, "Học sinh", "Con",
                    new DateOnly(2024, 3, 1), null, null]
            ]);
        return new ExportFile($"mau-nhap-nguoi-thue_{code}.xlsx", writer.Write(new SpreadsheetDocument([sheet])));
    }
}

/// <summary>RT-UC-10 bước 1: đọc file, kiểm từng người + từng phòng — <b>không lưu gì, server không giữ gì</b>.</summary>
public sealed record PreviewTenancyImportCommand(Guid PropertyId, byte[] Content) : IRequest<Result<TenancyImportPreviewDto>>;

public sealed class PreviewTenancyImportHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, ISpreadsheetReader reader,
    IValidator<CreateRenterCommand> renterValidator, TimeProvider clock)
    : IRequestHandler<PreviewTenancyImportCommand, Result<TenancyImportPreviewDto>>
{
    public async ValueTask<Result<TenancyImportPreviewDto>> Handle(PreviewTenancyImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var table = ImportFiles.Locate(reader, request.Content, TenancyImportTemplate.Columns, out var fileError);
        if (table is null)
            return fileError!;

        var rows = table.Rows.Select(TenancyImportTemplate.Parse).ToList();
        var check = await new TenancyImportChecker(db, currentUser, protector, renterValidator)
            .CheckAsync(property.Value!, rows, table.Rows.ToDictionary(r => r.RowNumber, r => r.Errors), clock.GetUtcNow().ToBusinessDate(),
                cancellationToken);
        var valid = check.Rooms.Count(r => r.IsValid);
        return new TenancyImportPreviewDto(valid, check.Rooms.Count - valid, check.Rows, check.Rooms,
            rows.Count == 0 ? [new ImportIssue("IMPORT_EMPTY", "File không có dòng dữ liệu nào.")] : []);
    }
}

/// <summary>
/// RT-UC-10 bước 2: nhận các dòng (đã sửa trên màn), kiểm lại toàn bộ, lưu các phòng hợp lệ — mỗi phòng 1 transaction: dùng lại / tạo người
/// thuê → HĐ (tính tiền từ đầu kỳ hiện tại của khu hoặc ngày vào ở, thiếu bản ký) → kích hoạt (chỉ số nhận phòng = số mới nhất).
/// </summary>
public sealed record SaveTenancyImportCommand(Guid PropertyId, IReadOnlyList<TenancyImportRow> Rows) : IRequest<Result<ImportSaveResultDto>>;

public sealed class SaveTenancyImportCommandValidator : AbstractValidator<SaveTenancyImportCommand>
{
    public SaveTenancyImportCommandValidator() =>
        RuleFor(x => x.Rows).NotEmpty().WithErrorCode("REQUIRED").Must(r => r is null || r.Count <= ImportErrors.MaxRows).WithErrorCode("IMPORT_TOO_MANY_ROWS");
}

public sealed class SaveTenancyImportHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, ISender sender, IAuditTrail auditTrail,
    IValidator<CreateRenterCommand> renterValidator, TimeProvider clock)
    : IRequestHandler<SaveTenancyImportCommand, Result<ImportSaveResultDto>>
{
    public async ValueTask<Result<ImportSaveResultDto>> Handle(SaveTenancyImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var check = await new TenancyImportChecker(db, currentUser, protector, renterValidator)
            .CheckAsync(property.Value!, request.Rows, new Dictionary<int, List<ImportIssue>>(), clock.GetUtcNow().ToBusinessDate(), cancellationToken);

        var units = new List<ImportUnitResult>();
        foreach (var room in check.Rooms)
        {
            if (!room.IsValid)
            {
                var rowErrors = check.Rows.Where(r => room.RowNumbers.Contains(r.Row.RowNumber)).SelectMany(r => r.Errors);
                units.Add(new ImportUnitResult(room.RoomCode, room.RowNumbers, ImportOutcome.Invalid, [.. room.Errors, .. rowErrors]));
                continue;
            }
            var plan = check.Plans.Single(p => p.RoomCode == room.RoomCode);
            var failure = await ImportFiles.SaveUnitAsync(db, () => SaveAsync(plan, cancellationToken), cancellationToken);
            units.Add(new ImportUnitResult(room.RoomCode, room.RowNumbers, failure.Count == 0 ? ImportOutcome.Saved : ImportOutcome.Failed, failure));
        }
        // Dòng thiếu mã phòng không thuộc phòng nào.
        foreach (var orphan in check.Rows.Where(r => r.Row.RoomCode is null))
            units.Add(new ImportUnitResult($"#{orphan.Row.RowNumber}", [orphan.Row.RowNumber], ImportOutcome.Invalid, orphan.Errors));
        return await ImportPreview.RecordAsync(db, auditTrail, request.PropertyId, "Tenancies", units, cancellationToken);
    }

    private async Task<Error?> SaveAsync(TenancyPlan plan, CancellationToken ct)
    {
        var ids = new Dictionary<int, Guid>();
        foreach (var row in plan.Rows)
        {
            var id = await ResolveRenterAsync(row, ct);
            if (id.IsFailure)
                return id.Error;
            ids[row.RowNumber] = id.Value;
        }

        var representative = plan.Rows.Single(r => TenancyImportTemplate.IsRepresentative(r.Role));
        var occupants = plan.Rows.Where(r => TenancyImportTemplate.IsLiving(r.Role))
            .Select(r => new OccupantRequest(ids[r.RowNumber], r.MoveInDate, null, null, null, r.Role == ImportRole.Occupant ? r.Relationship : null))
            .ToList();
        var input = new ContractInput(ids[representative.RowNumber], plan.StartDate, null, null, null, null, plan.MonthlyRent, plan.DepositAmount, null,
            plan.BillingStartDate == plan.StartDate ? null : plan.BillingStartDate, null, null, null, null, "Nhập từ Excel", occupants);
        var created = await sender.Send(new CreateContractCommand(plan.RoomId, null, input), ct);
        if (created.IsFailure)
            return created.Error;

        // Chỉ số nhận phòng = số mới nhất của từng công tơ đang đo tại ngày tính tiền (chỉ số khai ở form phòng).
        var meters = await db.Meters.AsNoTracking()
            .Where(m => m.RoomId == plan.RoomId && m.InstalledDate <= plan.BillingStartDate && (m.RemovedDate == null || m.RemovedDate > plan.BillingStartDate))
            .Select(m => m.Id).ToListAsync(ct);
        var activated = await sender.Send(new ActivateContractCommand(created.Value!.Id, meters.Select(m => new MeterReadingInput(m, null)).ToList()), ct);
        return activated.IsFailure ? activated.Error : null;
    }

    /// <summary>Có số giấy tờ trùng hồ sơ ⇒ dùng lại (kể cả hồ sơ vừa tạo ở phòng trước của lần import này); không thì tạo mới.</summary>
    private async Task<Result<Guid>> ResolveRenterAsync(TenancyImportRow row, CancellationToken ct)
    {
        if (row is { IdType: { } type, IdNumber: { } number })
        {
            var hash = protector.ProtectIdNumber(currentUser.OrganizationId!.Value, type, number).Hash;
            if (await db.Renters.Where(r => r.IdNumberHash == hash).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct) is { } existing)
                return existing;
        }
        return await sender.Send(new CreateRenterCommand(TenancyImportTemplate.Renter(row)), ct);
    }
}

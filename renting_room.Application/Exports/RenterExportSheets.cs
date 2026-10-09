using System.Globalization;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.Application.Exports;

/// <summary>Cột và cách chia sheet của E1 — Danh sách người thuê.</summary>
internal static class RenterExportSheets
{
    private const string Title = "DANH SÁCH NGƯỜI THUÊ";
    private const string NoFloor = "Không rõ tầng";

    private static readonly SpreadsheetColumn[] Columns =
    [
        new("STT", SpreadsheetColumnType.Number, 6),
        new("Khu", SpreadsheetColumnType.Text, 20),
        new("Tầng", SpreadsheetColumnType.Text, 8),
        new("Phòng", SpreadsheetColumnType.Text, 10),
        new("Số hợp đồng", SpreadsheetColumnType.Text, 15),
        new("Vai trò", SpreadsheetColumnType.Text, 11),
        new("Họ tên", SpreadsheetColumnType.Text, 24),
        new("Ngày sinh", SpreadsheetColumnType.Date, 12),
        new("Giới tính", SpreadsheetColumnType.Text, 9),
        new("Số điện thoại", SpreadsheetColumnType.Text, 14),
        new("Loại giấy tờ", SpreadsheetColumnType.Text, 11),
        new("Số giấy tờ", SpreadsheetColumnType.Text, 16),
        new("Ngày cấp", SpreadsheetColumnType.Date, 12),
        new("Nơi cấp", SpreadsheetColumnType.Text, 22),
        new("Quốc tịch", SpreadsheetColumnType.Text, 9),
        new("Địa chỉ thường trú", SpreadsheetColumnType.Text, 36),
        new("Nghề nghiệp", SpreadsheetColumnType.Text, 16),
        new("Nơi làm việc / học tập", SpreadsheetColumnType.Text, 22),
        new("Quan hệ với chủ hộ", SpreadsheetColumnType.Text, 22),
        new("Người chưa thành niên", SpreadsheetColumnType.Text, 24),
        new("Ngày vào ở", SpreadsheetColumnType.Date, 12),
        new("Ngày chuyển đi", SpreadsheetColumnType.Date, 13),
        new("Trạng thái hợp đồng", SpreadsheetColumnType.Text, 15),
        new("Liên hệ khẩn cấp", SpreadsheetColumnType.Text, 28)
    ];

    public static IReadOnlyList<SpreadsheetSheet> Build(
        IReadOnlyList<RenterExportRow> rows, RenterExportLayout layout, string subtitle, Func<RenterExportRow, string> idNumber)
    {
        if (rows.Count == 0 || layout == RenterExportLayout.SingleSheet)
            return [Sheet("Danh sách người thuê", Title, subtitle, rows, idNumber)];

        var groups = layout == RenterExportLayout.SheetPerFloor
            ? rows.GroupBy(r => (r.PropertyId, r.Floor)).Select(g => (
                Name: $"{g.First().PropertyCode} - {FloorLabel(g.Key.Floor)}",
                Title: $"{Title} — {g.First().PropertyName} — {FloorLabel(g.Key.Floor)}",
                Rows: g.ToList()))
            : rows.GroupBy(r => r.PropertyId).Select(g => (
                Name: g.First().PropertyName,
                Title: $"{Title} — {g.First().PropertyName}",
                Rows: g.ToList()));

        return groups.Select(g => Sheet(g.Name, g.Title, subtitle, g.Rows, idNumber)).ToList();
    }

    public static string Subtitle(DateOnly from, DateOnly to, DateOnly today, DateTimeOffset now, bool includeSensitive)
    {
        var period = from == to
            ? $"Người đang ở ngày {Format(from)}"
            : $"Người ở trong khoảng {Format(from)} – {Format(to)}";
        var exportedAt = now.ToOffset(VietnamTime.Offset).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
        var sensitivity = includeSensitive ? " · CÓ SỐ GIẤY TỜ ĐẦY ĐỦ — bảo mật theo Luật Bảo vệ dữ liệu cá nhân 2025" : string.Empty;
        return from == to && from == today
            ? $"{period} (hôm nay) · Xuất lúc {exportedAt}{sensitivity}"
            : $"{period} · Xuất lúc {exportedAt}{sensitivity}";
    }

    private static SpreadsheetSheet Sheet(
        string name, string title, string subtitle, IReadOnlyList<RenterExportRow> rows, Func<RenterExportRow, string> idNumber) =>
        new(name, title, subtitle, Columns, rows.Select((r, i) => ToCells(i + 1, r, idNumber)).ToList());

    private static object?[] ToCells(int index, RenterExportRow r, Func<RenterExportRow, string> idNumber) =>
    [
        index,
        r.PropertyName,
        r.Floor,
        r.RoomCode,
        r.ContractNo,
        r.IsRepresentative ? "Đại diện" : "Người ở",
        r.FullName,
        r.DateOfBirth,
        GenderLabel(r.Gender),
        r.Phone,
        IdTypeLabel(r.IdType),
        idNumber(r),
        r.IdIssueDate,
        r.IdIssuePlace,
        r.Nationality,
        r.PermanentAddress,
        r.Occupation,
        r.Workplace,
        RelationshipLabel(r),
        MinorNote(r),
        r.MoveInDate,
        r.MoveOutDate,
        StatusLabel(r.ContractStatus),
        EmergencyContact(r)
    ];

    private static string? RelationshipLabel(RenterExportRow r) => r switch
    {
        { IsHouseholdHead: true, IsRepresentative: true } => "Chủ hộ (người đứng tên)",
        { IsHouseholdHead: true } => "Chủ hộ",
        _ => OccupantRelationshipLabels.Describe(r.RelationshipType, r.Relationship)
    };

    /// <summary>Luật Cư trú Điều 28: tờ khai tạm trú của người chưa thành niên cần ý kiến đồng ý của cha, mẹ / người giám hộ.</summary>
    private static string? MinorNote(RenterExportRow r)
    {
        if (r.DateOfBirth.AgeOn(r.MoveInDate) >= 18)
            return null;
        if (r.RelationshipType is OccupantRelationship.Child or OccupantRelationship.AdoptedChild or OccupantRelationship.Ward)
            return "Có — cha/mẹ/giám hộ đứng tên HĐ";
        return r.GuardianConsent ? "Có — đã có đồng ý của cha mẹ/giám hộ" : "Có — CHƯA có đồng ý";
    }

    private static string FloorLabel(string? floor) => floor is null ? NoFloor : $"Tầng {floor}";

    private static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string GenderLabel(Gender gender) => gender switch
    {
        Gender.Male => "Nam",
        Gender.Female => "Nữ",
        _ => "Khác"
    };

    private static string IdTypeLabel(IdDocumentType? type) => type switch
    {
        null => "",
        IdDocumentType.CitizenId => "CCCD",
        IdDocumentType.LegacyId => "CMND",
        _ => "Hộ chiếu"
    };

    private static string StatusLabel(ContractStatus status) => status switch
    {
        ContractStatus.Active => "Đang hiệu lực",
        ContractStatus.Liquidating => "Đang thanh lý",
        _ => "Đã kết thúc"
    };

    private static string? EmergencyContact(RenterExportRow r) =>
        (r.EmergencyContactName, r.EmergencyContactPhone) switch
        {
            (null, null) => null,
            (var name, null) => name,
            (null, var phone) => phone,
            var (name, phone) => $"{name} — {phone}"
        };
}

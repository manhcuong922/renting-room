using renting_room.Application.Common.Interfaces;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;

namespace renting_room.Application.Imports;

/// <param name="ElectricityReading">Chỉ số đầu kỳ hiện tại — công tơ tính là lắp từ ngày chốt kỳ hiện tại của khu.</param>
public sealed record RoomImportRow(
    int RowNumber,
    string? Code,
    string? Floor,
    decimal? AreaM2,
    int? MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyList<string>? Amenities,
    string? Description,
    string? ElectricitySerial,
    decimal? ElectricityReading,
    string? WaterSerial,
    decimal? WaterReading);

internal static class RoomImportTemplate
{
    public static readonly ImportColumn[] Columns =
    [
        new("code", "Mã phòng*", SpreadsheetColumnType.Text, 12),
        new("floor", "Tầng", SpreadsheetColumnType.Text, 8),
        new("area", "Diện tích (m²)", SpreadsheetColumnType.Number, 12),
        new("people", "Số người (loại phòng)", SpreadsheetColumnType.Number, 12),
        new("listedRent", "Giá niêm yết", SpreadsheetColumnType.Money, 14),
        new("deposit", "Tiền cọc mặc định", SpreadsheetColumnType.Money, 14),
        new("amenities", "Tiện nghi", SpreadsheetColumnType.Text, 24),
        new("description", "Ghi chú", SpreadsheetColumnType.Text, 24),
        new("electricitySerial", "Số seri công tơ điện", SpreadsheetColumnType.Text, 16),
        new("electricityReading", "Chỉ số điện đầu kỳ", SpreadsheetColumnType.Number, 14),
        new("waterSerial", "Số seri công tơ nước", SpreadsheetColumnType.Text, 16),
        new("waterReading", "Chỉ số nước đầu kỳ", SpreadsheetColumnType.Number, 14)
    ];

    public static readonly Dictionary<string, string> Headers = Columns.ToDictionary(c => c.Key, c => c.Header.TrimEnd('*'));

    /// <summary>Tên thuộc tính lỗi của validator lệnh tạo phòng → cột trong file.</summary>
    public static readonly Dictionary<string, string> PropertyToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = "code", ["floor"] = "floor", ["areaM2"] = "area", ["maxOccupants"] = "people", ["listedRent"] = "listedRent",
        ["defaultDeposit"] = "deposit", ["amenities"] = "amenities", ["description"] = "description"
    };

    public static RoomSpecInput Spec(RoomImportRow row) =>
        new(row.Floor, row.AreaM2, row.MaxOccupants, row.ListedRent, row.DefaultDeposit, row.Amenities ?? [], row.Description);

    /// <summary>Đọc 1 dòng Excel thành dòng nhập (lỗi định dạng gom vào <paramref name="row"/>).</summary>
    public static RoomImportRow Parse(ImportRow row) => new(
        row.RowNumber,
        row.Text("code") is { } code ? TextNormalizer.NormalizeCode(code) : null,
        row.Text("floor"),
        row.Decimal("area"),
        row.Int("people"),
        row.Decimal("listedRent"),
        row.Decimal("deposit"),
        (row.Text("amenities") ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant()).Distinct().ToList(),
        row.Text("description"),
        row.Text("electricitySerial"),
        row.Decimal("electricityReading"),
        row.Text("waterSerial"),
        row.Decimal("waterReading"));
}

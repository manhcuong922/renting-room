namespace renting_room.Application.Common.Interfaces;

/// <summary>Ghi file Excel từ dữ liệu dạng bảng (M10). Hiện thực ở Infrastructure (ClosedXML).</summary>
public interface ISpreadsheetWriter
{
    byte[] Write(SpreadsheetDocument document);
}

public enum SpreadsheetColumnType
{
    Text,     // luôn ghi dạng chữ (giữ số 0 đầu SĐT / CCCD)
    Number,
    Money,    // #,##0
    Date      // dd/MM/yyyy
}

public sealed record SpreadsheetColumn(string Header, SpreadsheetColumnType Type, double Width);

/// <param name="Name">Tên sheet mong muốn — writer cắt ≤ 31 ký tự, bỏ ký tự cấm, chống trùng (RP-BR-06).</param>
/// <param name="Title">Dòng tiêu đề in đậm phía trên bảng.</param>
/// <param name="Subtitle">Dòng mô tả bộ lọc / ngày xuất.</param>
public sealed record SpreadsheetSheet(
    string Name,
    string Title,
    string? Subtitle,
    IReadOnlyList<SpreadsheetColumn> Columns,
    IReadOnlyList<object?[]> Rows);

public sealed record SpreadsheetDocument(IReadOnlyList<SpreadsheetSheet> Sheets);

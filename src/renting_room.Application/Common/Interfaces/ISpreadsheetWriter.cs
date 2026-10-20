namespace renting_room.Application.Common.Interfaces;

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

public sealed record SpreadsheetSheet(
    string Name,
    string Title,
    string? Subtitle,
    IReadOnlyList<SpreadsheetColumn> Columns,
    IReadOnlyList<object?[]> Rows);

public sealed record SpreadsheetDocument(IReadOnlyList<SpreadsheetSheet> Sheets);

using ClosedXML.Excel;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Exports;

/// <summary>
/// Ghi xlsx bằng ClosedXML: tiêu đề, dòng mô tả, header cố định + auto-filter, kiểu dữ liệu tường minh.
/// RP-BR-02: chuỗi bắt đầu bằng = + - @ Tab CR được đánh dấu quote-prefix ⇒ Excel luôn coi là chữ, không chạy công thức.
/// RP-BR-06: tên sheet ≤ 31 ký tự, bỏ ký tự cấm, trùng thì thêm "(2)".
/// </summary>
internal sealed class ClosedXmlSpreadsheetWriter : ISpreadsheetWriter
{
    private const int TitleRow = 1;
    private const int SubtitleRow = 2;
    private const int HeaderRow = 4;
    private const int MaxSheetNameLength = 31;
    private static readonly char[] ForbiddenSheetChars = ['[', ']', ':', '*', '?', '/', '\\'];
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    public byte[] Write(SpreadsheetDocument document)
    {
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in document.Sheets)
            WriteSheet(workbook.Worksheets.Add(UniqueSheetName(sheet.Name, usedNames)), sheet);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSheet(IXLWorksheet ws, SpreadsheetSheet sheet)
    {
        var columnCount = sheet.Columns.Count;

        ws.Cell(TitleRow, 1).Value = sheet.Title;
        ws.Range(TitleRow, 1, TitleRow, columnCount).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        if (sheet.Subtitle is not null)
        {
            ws.Cell(SubtitleRow, 1).Value = sheet.Subtitle;
            ws.Range(SubtitleRow, 1, SubtitleRow, columnCount).Merge().Style.Font.SetItalic();
        }

        for (var c = 0; c < columnCount; c++)
        {
            var column = sheet.Columns[c];
            ws.Cell(HeaderRow, c + 1).Value = column.Header;
            ws.Column(c + 1).Width = column.Width;
            ApplyColumnFormat(ws.Column(c + 1).Cells(HeaderRow + 1, HeaderRow + Math.Max(sheet.Rows.Count, 1)), column.Type);
        }

        var header = ws.Range(HeaderRow, 1, HeaderRow, columnCount);
        header.Style.Font.SetBold()
            .Fill.SetBackgroundColor(XLColor.FromHtml("#D9E1F2"))
            .Alignment.SetWrapText()
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
            .Border.SetInsideBorder(XLBorderStyleValues.Thin);

        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            for (var c = 0; c < columnCount; c++)
                SetValue(ws.Cell(HeaderRow + 1 + r, c + 1), sheet.Columns[c].Type, row[c]);
        }

        var lastRow = HeaderRow + sheet.Rows.Count;
        ws.Range(HeaderRow, 1, lastRow, columnCount).SetAutoFilter();
        ws.SheetView.FreezeRows(HeaderRow);
    }

    private static void ApplyColumnFormat(IXLCells cells, SpreadsheetColumnType type)
    {
        switch (type)
        {
            case SpreadsheetColumnType.Text:
                cells.Style.NumberFormat.Format = "@";
                break;
            case SpreadsheetColumnType.Money:
                cells.Style.NumberFormat.Format = "#,##0";
                break;
            case SpreadsheetColumnType.Date:
                cells.Style.NumberFormat.Format = "dd/MM/yyyy";
                break;
        }
    }

    private static void SetValue(IXLCell cell, SpreadsheetColumnType type, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case DateOnly date when type == SpreadsheetColumnType.Date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                return;
            case int number when type != SpreadsheetColumnType.Text:
                cell.Value = number;
                return;
            case decimal number when type != SpreadsheetColumnType.Text:
                cell.Value = (double)number;
                return;
        }

        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        cell.Value = text;
        if (text.Length > 0 && Array.IndexOf(FormulaTriggers, text[0]) >= 0)
            cell.Style.IncludeQuotePrefix = true;
    }

    internal static string UniqueSheetName(string requested, ISet<string> used)
    {
        var cleaned = new string(requested.Where(ch => Array.IndexOf(ForbiddenSheetChars, ch) < 0).ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0)
            cleaned = "Sheet";
        if (cleaned.Length > MaxSheetNameLength)
            cleaned = cleaned[..MaxSheetNameLength].TrimEnd();

        var candidate = cleaned;
        for (var i = 2; !used.Add(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = cleaned[..Math.Min(cleaned.Length, MaxSheetNameLength - suffix.Length)].TrimEnd() + suffix;
        }

        return candidate;
    }
}

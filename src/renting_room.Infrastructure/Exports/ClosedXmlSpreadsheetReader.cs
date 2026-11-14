using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Exports;

/// <summary>
/// Đọc xlsx bằng ClosedXML — chỉ lấy giá trị, ô công thức bị đánh dấu (RP-BR-02). Trước khi mở: kiểm cấu trúc zip của xlsx và tổng
/// dung lượng giải nén (chống zip bomb — file nhỏ bung ra hàng GB làm cạn RAM). Khi đọc: chỉ duyệt ô có dữ liệu, kẹp cột / sheet / dòng
/// (H-05 — trước đây mỗi dòng duyệt tới cột xa nhất của sheet: 1 ô ở cột XFD × 50.000 dòng ≈ 820 triệu ô).
/// </summary>
internal sealed class ClosedXmlSpreadsheetReader : ISpreadsheetReader
{
    private const int MaxZipEntries = 500;

    public IReadOnlyList<SheetData> Read(byte[] content, int maxRows)
    {
        EnsureSafeXlsx(content);
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(content));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException("Không đọc được file Excel (.xlsx).", ex);
        }

        using (workbook)
        {
            return workbook.Worksheets.Take(ISpreadsheetReader.MaxSheets).Select(ws => ReadSheet(ws, maxRows)).ToList();
        }
    }

    private static SheetData ReadSheet(IXLWorksheet ws, int maxRows)
    {
        var rows = new List<SheetRow>();
        foreach (var row in ws.RowsUsed())
        {
            var used = row.CellsUsed().Where(c => c.Address.ColumnNumber <= ISpreadsheetReader.MaxColumns).ToList();
            if (used.Count == 0)
                continue; // chỉ có dữ liệu ngoài vùng đọc ⇒ coi như dòng trống
            if (rows.Count == maxRows)
                return new SheetData(ws.Name, rows, Truncated: true);

            var cells = new string?[used.Max(c => c.Address.ColumnNumber)];
            var hasFormula = false;
            foreach (var cell in used)
            {
                if (cell.HasFormula)
                {
                    hasFormula = true;
                    continue;
                }
                cells[cell.Address.ColumnNumber - 1] = Text(cell.Value);
            }
            rows.Add(new SheetRow(row.RowNumber(), cells, hasFormula));
        }
        return new SheetData(ws.Name, rows);
    }

    /// <summary>xlsx = file zip có <c>xl/workbook.xml</c>; tổng dung lượng khai báo của các phần ≤ <see cref="ISpreadsheetReader.MaxUncompressedBytes"/>, ≤ 500 phần.</summary>
    private static void EnsureSafeXlsx(byte[] content)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("File không phải .xlsx.", ex);
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxZipEntries || zip.Entries.All(e => e.FullName != "xl/workbook.xml"))
                throw new InvalidDataException("File không phải .xlsx.");
            if (zip.Entries.Sum(e => e.Length) > ISpreadsheetReader.MaxUncompressedBytes)
                throw new SpreadsheetTooLargeException("File giải nén quá lớn.");
        }
    }

    private static string? Text(XLCellValue value)
    {
        var text = value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "TRUE" : "FALSE",
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

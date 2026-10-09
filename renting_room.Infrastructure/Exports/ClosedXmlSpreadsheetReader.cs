using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Exports;

/// <summary>
/// Đọc xlsx bằng ClosedXML — chỉ lấy giá trị, ô công thức bị đánh dấu (RP-BR-02). Trước khi mở: kiểm cấu trúc zip của xlsx và tổng
/// dung lượng giải nén (chống zip bomb — file nhỏ bung ra hàng GB làm cạn RAM).
/// </summary>
internal sealed class ClosedXmlSpreadsheetReader : ISpreadsheetReader
{
    private const int MaxZipEntries = 500;

    public IReadOnlyList<SheetData> Read(byte[] content)
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
            return workbook.Worksheets.Select(ws =>
            {
                var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                var rows = ws.RowsUsed().Select(row =>
                {
                    var cells = new string?[lastColumn];
                    var hasFormula = false;
                    for (var c = 1; c <= lastColumn; c++)
                    {
                        var cell = row.Cell(c);
                        if (cell.HasFormula)
                        {
                            hasFormula = true;
                            continue;
                        }
                        cells[c - 1] = Text(cell.Value);
                    }
                    return new SheetRow(row.RowNumber(), cells, hasFormula);
                }).ToList();
                return new SheetData(ws.Name, rows);
            }).ToList();
        }
    }

    /// <summary>xlsx = file zip có <c>xl/workbook.xml</c>; tổng dung lượng khai báo của các phần ≤ 20 MB, ≤ 500 phần.</summary>
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

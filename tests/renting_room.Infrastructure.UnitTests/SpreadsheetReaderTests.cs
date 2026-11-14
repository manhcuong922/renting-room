using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.UnitTests;

/// <summary>
/// H-05 (06/11/2026): đọc Excel import không được làm cạn RAM server. Trước đây mỗi dòng cấp mảng bằng cột xa nhất của cả sheet (tới 16.384 cột)
/// và đọc hết mọi dòng / mọi sheet rồi mới kiểm 500 dòng ⇒ 1 ô rác ở cột XFD × 50.000 dòng ≈ hàng GB, sập server cho mọi tổ chức.
/// </summary>
public sealed class SpreadsheetReaderTests
{
    private static ISpreadsheetReader Reader()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=x" })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider().GetRequiredService<ISpreadsheetReader>();
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void CellInLastExcelColumn_IsIgnored_EveryRowHoldsAtMostMaxColumns()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Phòng");
        ws.Cell(1, 1).Value = "Mã phòng*";
        ws.Cell(1, 3).Value = "Tầng";
        ws.Cell(1, 16_384).Value = "rác"; // cột XFD
        for (var r = 2; r <= 200; r++)
            ws.Cell(r, 1).Value = $"P{r}";

        var sheet = Reader().Read(Save(workbook), maxRows: 1_000).Single();

        sheet.Truncated.Should().BeFalse();
        sheet.Rows.Should().HaveCount(200);
        sheet.Rows.Should().OnlyContain(r => r.Cells.Count <= ISpreadsheetReader.MaxColumns);
        sheet.Rows[0].Cells.Should().Equal("Mã phòng*", null, "Tầng");
        sheet.Rows[1].Cells.Should().Equal("P2");
    }

    [Fact]
    public void StopsAfterMaxRows_AndMarksSheetTruncated()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Phòng");
        for (var r = 1; r <= 3_000; r++)
            ws.Cell(r, 1).Value = $"P{r}";

        var sheet = Reader().Read(Save(workbook), maxRows: 510).Single();

        sheet.Truncated.Should().BeTrue();
        sheet.Rows.Should().HaveCount(510);
        sheet.Rows[^1].RowNumber.Should().Be(510);
    }

    [Fact]
    public void RowsWithDataOnlyBeyondMaxColumns_AreSkipped_NotCountedTowardsLimit()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Phòng");
        ws.Cell(1, 1).Value = "Mã phòng*";
        for (var r = 2; r <= 600; r++)
            ws.Cell(r, ISpreadsheetReader.MaxColumns + 1).Value = "ghi chú ngoài mẫu";
        ws.Cell(601, 1).Value = "P1";

        var sheet = Reader().Read(Save(workbook), maxRows: 510).Single();

        sheet.Truncated.Should().BeFalse();
        sheet.Rows.Select(r => r.RowNumber).Should().Equal(1, 601);
    }

    [Fact]
    public void ReadsOnlyFirstMaxSheets()
    {
        using var workbook = new XLWorkbook();
        for (var i = 1; i <= ISpreadsheetReader.MaxSheets + 2; i++)
            workbook.Worksheets.Add($"Sheet {i}").Cell(1, 1).Value = i;

        Reader().Read(Save(workbook), maxRows: 10).Select(s => s.Name).Should().HaveCount(ISpreadsheetReader.MaxSheets)
            .And.StartWith("Sheet 1");
    }

    [Fact]
    public void UncompressedSizeAboveLimit_IsRejectedBeforeOpening()
    {
        using var bomb = new MemoryStream();
        using (var zip = new ZipArchive(bomb, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("xl/workbook.xml", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 6; i++)
                entry.Write(zeros);
        }

        var act = () => Reader().Read(bomb.ToArray(), maxRows: 10);

        act.Should().Throw<SpreadsheetTooLargeException>();
        ISpreadsheetReader.MaxUncompressedBytes.Should().Be(5 * 1024 * 1024);
    }
}

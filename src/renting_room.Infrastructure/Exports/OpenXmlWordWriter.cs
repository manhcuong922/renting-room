using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.Exports;

/// <summary>
/// Ghi .docx theo thể thức văn bản thường dùng ở VN: khổ A4, lề trái 3cm / phải 2cm / trên dưới 2cm,
/// Times New Roman 13. Mở / sửa / in được bằng Word, LibreOffice, Google Docs.
/// </summary>
internal sealed class OpenXmlWordWriter : IWordDocumentWriter
{
    private const string Font = "Times New Roman";
    private const int TwipsPerCm = 567;

    public byte[] Write(PrintDocument document)
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            var body = new Body();

            if (document.Watermark is not null)
                body.Append(Paragraph(new PrintParagraph(document.Watermark, PrintAlign.Center, Bold: true, FontSize: 12), color: "C00000"));

            foreach (var block in document.Blocks)
                body.Append(Render(block));

            body.Append(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 }, // A4
                new PageMargin
                {
                    Top = 2 * TwipsPerCm, Bottom = 2 * TwipsPerCm,
                    Left = (uint)(3 * TwipsPerCm), Right = (uint)(2 * TwipsPerCm),
                    Header = 720, Footer = 720, Gutter = 0
                }));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static IEnumerable<OpenXmlElement> Render(PrintBlock block) => block switch
    {
        PrintParagraph p => SplitLines(p).Select(line => (OpenXmlElement)Paragraph(line)),
        PrintTable t => [Table(t), Paragraph(new PrintParagraph(""))],
        PrintSignatures s => [Signatures(s)],
        PrintPageBreak => [new Paragraph(new Run(new Break { Type = BreakValues.Page }))],
        _ => []
    };

    /// <summary>Mỗi dòng "\n" thành một đoạn riêng để căn lề đúng.</summary>
    private static IEnumerable<PrintParagraph> SplitLines(PrintParagraph p) =>
        p.Text.Split('\n').Select(line => p with { Text = line.TrimEnd() });

    private static Paragraph Paragraph(PrintParagraph p, string? color = null)
    {
        var props = new RunProperties(
            new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font, EastAsia = Font },
            new FontSize { Val = (p.FontSize * 2).ToString() });
        if (p.Bold)
            props.Append(new Bold());
        if (p.Italic)
            props.Append(new Italic());
        if (color is not null)
            props.Append(new Color { Val = color });

        var justification = p.Align switch
        {
            PrintAlign.Center => JustificationValues.Center,
            PrintAlign.Left => JustificationValues.Left,
            _ => JustificationValues.Both
        };
        return new Paragraph(
            new ParagraphProperties(new Justification { Val = justification }, new SpacingBetweenLines { After = "80", Line = "300", LineRule = LineSpacingRuleValues.Auto }),
            new Run(props, new Text(p.Text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table Table(PrintTable t)
    {
        var border = new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 });
        var table = new Table(new TableProperties(border, new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));

        table.Append(Row(t.Headers, bold: true));
        foreach (var row in t.Rows)
            table.Append(Row(row, bold: false));
        return table;
    }

    private static TableRow Row(IEnumerable<string> cells, bool bold) =>
        new(cells.Select(text => new TableCell(Paragraph(new PrintParagraph(text, PrintAlign.Left, Bold: bold, FontSize: 12)))));

    private static Table Signatures(PrintSignatures s)
    {
        TableCell Cell(string title, string? name) => new(
            Paragraph(new PrintParagraph(title, PrintAlign.Center, Bold: true)),
            Paragraph(new PrintParagraph("(Ký, ghi rõ họ tên)", PrintAlign.Center, Italic: true, FontSize: 12)),
            Paragraph(new PrintParagraph("")), Paragraph(new PrintParagraph("")), Paragraph(new PrintParagraph("")),
            Paragraph(new PrintParagraph(name ?? "", PrintAlign.Center, Bold: true)));

        return new Table(
            new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }),
            new TableRow(Cell(s.LeftTitle, s.LeftName), Cell(s.RightTitle, s.RightName)));
    }
}

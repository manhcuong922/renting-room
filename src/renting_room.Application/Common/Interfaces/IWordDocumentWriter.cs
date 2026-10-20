namespace renting_room.Application.Common.Interfaces;

public interface IWordDocumentWriter
{
    byte[] Write(PrintDocument document);
}

public enum PrintAlign
{
    Left,
    Center,
    Justify
}

public abstract record PrintBlock;

public sealed record PrintParagraph(
    string Text, PrintAlign Align = PrintAlign.Justify, bool Bold = false, bool Italic = false, int FontSize = 13) : PrintBlock;

public sealed record PrintTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : PrintBlock;

public sealed record PrintSignatures(string LeftTitle, string RightTitle, string? LeftName, string? RightName) : PrintBlock;

public sealed record PrintPageBreak : PrintBlock;

/// <param name="Watermark">Dòng cảnh báo đỏ đầu trang (VD "BẢN NHÁP") — null với văn bản chính thức.</param>
public sealed record PrintDocument(string? Watermark, IReadOnlyList<PrintBlock> Blocks);

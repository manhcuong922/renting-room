namespace renting_room.Domain.Billing;

/// <param name="RoundTotal">Khu bật làm tròn tổng phiếu (BL-BR-29) — phiếu ghi lại để sửa tay / thêm dòng sau vẫn làm tròn đúng.</param>
public sealed record InvoiceCalculation(
    IReadOnlyList<CalculatedLine> Lines, IReadOnlyList<CalculatedSegment> Segments, IReadOnlyList<InvoiceIssue> Issues, bool RoundTotal = false);

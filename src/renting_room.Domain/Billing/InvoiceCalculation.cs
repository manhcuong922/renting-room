namespace renting_room.Domain.Billing;

/// <summary>Dòng hệ thống tính ra (chưa gắn vào phiếu).</summary>
public sealed record CalculatedLine(
    InvoiceLineType Type,
    Guid? FeeTypeId,
    string Description,
    string? Unit,
    DateOnly ServiceFrom,
    DateOnly ServiceTo,
    decimal Quantity,
    decimal UnitPrice,
    decimal? ProrationFactor,
    decimal Amount,
    int SortOrder)
{
    /// <summary>Nhận diện dòng hệ thống giữa các lần tính — gồm ngày đầu khoảng dịch vụ vì phiếu quyết toán có thể có 2 dòng cùng khoản (BL-BR-05).</summary>
    public (InvoiceLineType, Guid?, DateOnly) Key => (Type, FeeTypeId, ServiceFrom);
}

/// <param name="RoomId">Phòng của công tơ — cùng <paramref name="RoomCode"/>, <paramref name="MeterSerial"/>, ngày 2 chỉ số là bản chụp để hiển thị
/// từng phòng dưới dòng Điện nước (BL-BR-05).</param>
/// <param name="UsageStart">Ngày đầu kỳ sử dụng của dòng chứa đoạn đo (= <see cref="CalculatedLine.ServiceFrom"/>).</param>
public sealed record CalculatedSegment(
    Guid FeeTypeId, Guid MeterId, string? MeterSerial, Guid StartReadingId, Guid EndReadingId, decimal StartValue, decimal EndValue,
    Guid? RoomId = null, string? RoomCode = null, DateOnly? StartDate = null, DateOnly? EndDate = null, DateOnly? UsageStart = null);

/// <param name="RoundTotal">Khu bật làm tròn tổng phiếu (BL-BR-29) — phiếu ghi lại để sửa tay / thêm dòng sau vẫn làm tròn đúng.</param>
public sealed record InvoiceCalculation(
    IReadOnlyList<CalculatedLine> Lines, IReadOnlyList<CalculatedSegment> Segments, IReadOnlyList<InvoiceIssue> Issues, bool RoundTotal = false);

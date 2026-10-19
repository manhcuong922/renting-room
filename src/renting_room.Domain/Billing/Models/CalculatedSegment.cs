namespace renting_room.Domain.Billing;

/// <param name="RoomId">Phòng của công tơ — cùng <paramref name="RoomCode"/>, <paramref name="MeterSerial"/>, ngày 2 chỉ số là bản chụp để hiển thị
/// từng phòng dưới dòng Điện nước (BL-BR-05).</param>
/// <param name="UsageStart">Ngày đầu kỳ sử dụng của dòng chứa đoạn đo (= <see cref="CalculatedLine.ServiceFrom"/>).</param>
public sealed record CalculatedSegment(
    Guid FeeTypeId, Guid MeterId, string? MeterSerial, Guid StartReadingId, Guid EndReadingId, decimal StartValue, decimal EndValue,
    Guid? RoomId = null, string? RoomCode = null, DateOnly? StartDate = null, DateOnly? EndDate = null, DateOnly? UsageStart = null);

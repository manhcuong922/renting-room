using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

/// <summary>Đoạn đo của dòng điện nước: (chỉ số đầu, chỉ số cuối) trên 1 công tơ — thay công tơ giữa kỳ ⇒ nhiều đoạn (MT-BR-15).</summary>
public sealed class InvoiceMeterSegment : TenantEntity
{
    private InvoiceMeterSegment() { } // EF Core

    internal InvoiceMeterSegment(Guid invoiceId, CalculatedSegment segment)
    {
        Id = Guid.CreateVersion7();
        InvoiceId = invoiceId;
        FeeTypeId = segment.FeeTypeId;
        MeterId = segment.MeterId;
        StartReadingId = segment.StartReadingId;
        EndReadingId = segment.EndReadingId;
        StartValue = segment.StartValue;
        EndValue = segment.EndValue;
        Consumption = segment.EndValue - segment.StartValue;
        RoomId = segment.RoomId;
        RoomCode = segment.RoomCode;
        MeterSerial = segment.MeterSerial;
        StartDate = segment.StartDate;
        EndDate = segment.EndDate;
        UsageStart = segment.UsageStart;
    }

    public Guid InvoiceId { get; private set; }
    public Guid FeeTypeId { get; private set; }
    public Guid MeterId { get; private set; }
    public Guid StartReadingId { get; private set; }
    public Guid EndReadingId { get; private set; }
    public decimal StartValue { get; private set; }
    public decimal EndValue { get; private set; }
    public decimal Consumption { get; private set; }
    public bool Voided { get; private set; }

    // Bản chụp để hiển thị từng phòng / công tơ dưới dòng Điện nước (BL-BR-05, 18/10/2026) — NULL ở đoạn đo lập trước khi có cột.
    public Guid? RoomId { get; private set; }
    public string? RoomCode { get; private set; }
    public string? MeterSerial { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>Ngày đầu kỳ sử dụng của dòng chứa đoạn đo — phiếu quyết toán có 2 dòng cùng khoản thì mỗi đoạn thuộc đúng 1 dòng.</summary>
    public DateOnly? UsageStart { get; private set; }

    /// <summary>Đoạn đo thuộc dòng Điện nước này (dữ liệu cũ không có <see cref="UsageStart"/> ⇒ theo khoản thu).</summary>
    public bool BelongsTo(InvoiceLine line) => FeeTypeId == line.FeeTypeId && (UsageStart is null || UsageStart == line.ServiceFrom);

    internal void Void() => Voided = true;
}

using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

public enum InvoiceType
{
    Regular,
    Final
}

public enum InvoiceStatus
{
    Draft,
    Finalized,
    Void
}

/// <summary>Nhóm dòng trên phiếu (M07): Tiền phòng · Điện nước · Dịch vụ · Phụ thu · Giảm trừ · Hoàn trả.</summary>
public enum InvoiceLineType
{
    Rent,
    Metered,
    Service,
    Surcharge,
    ManualDiscount,

    /// <summary>Hoàn trả cho người thuê (BL-BR-27) — nhập tay như phụ thu, lưu số âm, được làm tổng phiếu âm.</summary>
    Refund
}

/// <summary>Trạng thái thu tiền (dẫn xuất, chỉ phiếu đã chốt).</summary>
public enum InvoicePaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Overdue,

    /// <summary>Đã đóng bằng "bỏ nợ" (PM-BR-16) — không tính doanh thu.</summary>
    WrittenOff,

    /// <summary>Tổng âm (hoàn trả lớn hơn phần thu) — chủ trọ phải trả lại người thuê, chưa xác nhận (BL-BR-27).</summary>
    RefundPending,

    /// <summary>Tổng âm, chủ trọ đã xác nhận trả lại người thuê.</summary>
    Refunded
}

/// <summary>Vấn đề của phiếu nháp: <c>Error</c> chặn chốt (thiếu chỉ số, thiếu giá…), <c>Warning</c> chỉ nhắc.</summary>
public sealed record InvoiceIssue(string Code, string Severity, string Message, Guid? Ref = null)
{
    public const string Error = "Error";
    public const string Warning = "Warning";
}

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
    public (InvoiceLineType, Guid?) Key => (Type, FeeTypeId);
}

public sealed record CalculatedSegment(
    Guid FeeTypeId, Guid MeterId, string? MeterSerial, Guid StartReadingId, Guid EndReadingId, decimal StartValue, decimal EndValue);

public sealed record InvoiceCalculation(
    IReadOnlyList<CalculatedLine> Lines, IReadOnlyList<CalculatedSegment> Segments, IReadOnlyList<InvoiceIssue> Issues);

/// <summary>
/// Phiếu báo tiền phòng (M07) — không phải hóa đơn GTGT (LEG-08). Nháp sửa được (sửa tay, phụ thu, tính lại);
/// chốt xong bất biến (BL-BR-14), chỉ hủy được khi chưa thu tiền (BL-BR-15).
/// </summary>
public sealed class Invoice : TenantEntity
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<InvoiceMeterSegment> _segments = [];

    private Invoice() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ContractId { get; private set; }
    public InvoiceType Type { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>Ngày 1 của tháng thu (tháng của <see cref="PeriodStart"/> — C-05).</summary>
    public DateOnly BillingMonth { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string? InvoiceNo { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }

    /// <summary>Σ dòng hoàn trả (≤ 0) — tách khỏi giảm trừ vì được làm tổng phiếu âm (BL-BR-27).</summary>
    public decimal RefundTotal { get; private set; }

    /// <summary>Subtotal + DiscountTotal + RefundTotal; âm ⇒ chủ trọ phải trả lại người thuê.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Tổng đã thu — M08 cập nhật trong cùng transaction với phân bổ (PM-BR-05).</summary>
    public decimal PaidAmount { get; private set; }

    /// <summary>Phần đã đóng bằng "bỏ nợ" (nằm trong <see cref="PaidAmount"/>) — loại khỏi doanh thu (PM-BR-16).</summary>
    public decimal WrittenOffAmount { get; private set; }
    public List<InvoiceIssue> Issues { get; private set; } = [];
    public string SnapshotRoomCode { get; private set; } = null!;
    public string SnapshotContractNo { get; private set; } = null!;
    public string SnapshotRepresentativeName { get; private set; } = null!;
    public string? Note { get; private set; }
    public DateTimeOffset? FinalizedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Xác nhận đã trả lại người thuê phần tổng âm (BL-BR-27).</summary>
    public DateOnly? RefundedOn { get; private set; }
    public PaymentMethod? RefundMethod { get; private set; }
    public string? RefundNote { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public IReadOnlyList<InvoiceMeterSegment> Segments => _segments;

    /// <summary>Còn nợ (≥ 0) — phiếu tổng âm không bù trừ nợ của phiếu khác.</summary>
    public decimal Outstanding => Status == InvoiceStatus.Finalized ? Math.Max(TotalAmount - PaidAmount, 0) : 0;

    /// <summary>Số chủ trọ còn phải trả lại người thuê (phiếu đã chốt, tổng âm, chưa xác nhận đã hoàn).</summary>
    public decimal RefundDue => Status == InvoiceStatus.Finalized && TotalAmount < 0 && RefundedOn is null ? -TotalAmount : 0;

    /// <summary>Phần thu sau giảm trừ (không tính hoàn trả) — luôn phải ≥ 0 (BL-BR-10).</summary>
    public decimal NetCharges => Subtotal + DiscountTotal;
    public bool HasBlockingIssues => Issues.Any(i => i.Severity == InvoiceIssue.Error);

    public static Invoice CreateDraft(
        Guid propertyId, Guid roomId, Guid contractId, BillingPeriod period,
        string roomCode, string contractNo, string representativeName, InvoiceCalculation calculation, InvoiceType type = InvoiceType.Regular)
    {
        var invoice = new Invoice
        {
            Id = Guid.CreateVersion7(),
            PropertyId = propertyId,
            RoomId = roomId,
            ContractId = contractId,
            Type = type,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            // C-05: tháng thu = tháng của kỳ chuẩn của khu chứa kỳ (kỳ đầu lẻ 03/11–04/11 của khu chốt ngày 5 ⇒ tháng 10).
            BillingMonth = period.Month,
            Status = InvoiceStatus.Draft,
            SnapshotRoomCode = roomCode,
            SnapshotContractNo = contractNo,
            SnapshotRepresentativeName = representativeName
        };
        invoice.ApplyCalculation(calculation, keepManualEdits: false);
        return invoice;
    }

    public InvoicePaymentStatus? PaymentStatus(DateOnly today)
    {
        if (Status != InvoiceStatus.Finalized)
            return null;
        if (TotalAmount < 0)
            return RefundedOn is null ? InvoicePaymentStatus.RefundPending : InvoicePaymentStatus.Refunded;
        if (PaidAmount >= TotalAmount)
            return WrittenOffAmount > 0 ? InvoicePaymentStatus.WrittenOff : InvoicePaymentStatus.Paid;
        if (DueDate < today)
            return InvoicePaymentStatus.Overdue;
        return PaidAmount > 0 ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
    }

    /// <summary>
    /// BL-BR-07 / BL-UC-06: thay dòng hệ thống bằng kết quả tính mới. Ô sửa tay được giữ (nếu <paramref name="keepManualEdits"/>) và
    /// cập nhật giá trị hệ thống bên cạnh — hệ thống đổi so với lúc sửa ⇒ cảnh báo <c>EDITED_BASE_CHANGED</c>. Phụ thu / giảm tay luôn giữ.
    /// </summary>
    /// <param name="periodEnd">Kỳ bị cắt ngắn sau khi tạo nháp (HĐ bắt đầu thanh lý) ⇒ cập nhật ngày cuối kỳ.</param>
    public Result ApplyCalculation(InvoiceCalculation calculation, bool keepManualEdits, DateOnly? periodEnd = null)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        PeriodEnd = periodEnd ?? PeriodEnd;

        var issues = calculation.Issues.ToList();
        var existing = _lines.Where(l => l.IsSystem).ToDictionary(l => l.Key);
        _lines.RemoveAll(l => l.IsSystem && calculation.Lines.All(c => c.Key != l.Key));

        foreach (var line in calculation.Lines)
        {
            if (existing.TryGetValue(line.Key, out var current))
            {
                if (keepManualEdits && current.IsManuallyEdited)
                {
                    if (current.SystemQuantity != line.Quantity || current.SystemUnitPrice != line.UnitPrice || current.SystemAmount != line.Amount)
                        issues.Add(new InvoiceIssue("EDITED_BASE_CHANGED", InvoiceIssue.Warning,
                            $"\"{line.Description}\" đã sửa tay nhưng số hệ thống tính lại đã đổi ({current.SystemAmount:N0} → {line.Amount:N0}) — kiểm tra lại.",
                            current.Id));
                    current.RefreshSystem(line);
                }
                else
                {
                    current.Reset(line);
                }
            }
            else
            {
                _lines.Add(InvoiceLine.FromCalculation(Id, line));
            }
        }

        _segments.Clear();
        _segments.AddRange(calculation.Segments.Select(s => new InvoiceMeterSegment(Id, s)));
        Issues = issues;
        Recompute();
        if (NetCharges < 0)
            Issues = [.. Issues, new InvoiceIssue("NEGATIVE_TOTAL", InvoiceIssue.Error, "Giảm trừ lớn hơn phần thu — giảm bớt dòng giảm trừ.")];
        return Result.Success();
    }

    /// <summary>BL-BR-12: phần hệ thống của nháp (bỏ qua ô sửa tay — so giá trị hệ thống) khớp kết quả tính lại.</summary>
    public bool SystemPartMatches(InvoiceCalculation calculation)
    {
        var system = _lines.Where(l => l.IsSystem).ToList();
        if (system.Count != calculation.Lines.Count)
            return false;
        foreach (var line in calculation.Lines)
        {
            var current = system.FirstOrDefault(l => l.Key == line.Key);
            if (current is null
                || (current.IsManuallyEdited ? current.SystemQuantity : current.Quantity) != line.Quantity
                || (current.IsManuallyEdited ? current.SystemUnitPrice : current.UnitPrice) != line.UnitPrice
                || (current.IsManuallyEdited ? current.SystemAmount : current.Amount) != line.Amount)
                return false;
        }
        return _segments.Count == calculation.Segments.Count
            && calculation.Segments.All(s => _segments.Any(x => x.MeterId == s.MeterId && x.StartReadingId == s.StartReadingId && x.EndReadingId == s.EndReadingId));
    }

    /// <summary>BL-BR-07: sửa tay dòng hệ thống. Thành tiền bỏ trống ⇒ = số lượng × đơn giá × hệ số prorate.</summary>
    public Result EditLine(Guid lineId, decimal? quantity, decimal? unitPrice, decimal? amount, string? note)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (!line.IsSystem)
            return UpdateManualLine(lineId, line.Description, quantity, unitPrice, amount ?? Math.Abs(line.Amount), note);
        if (line.Type == InvoiceLineType.Rent && string.IsNullOrWhiteSpace(note))
            return Result.Failure(BillingErrors.NoteRequired);

        var q = quantity ?? line.Quantity;
        var p = unitPrice ?? line.UnitPrice;
        var newAmount = amount ?? Money(q * p * (line.ProrationFactor ?? 1));
        if (NetCharges - line.Amount + newAmount < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        line.Edit(q, p, newAmount, note);
        Recompute();
        return Result.Success();
    }

    public Result ResetLine(Guid lineId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (!line.IsSystem)
            return RemoveManualLine(lineId);

        line.ClearEdit();
        Recompute();
        return Result.Success();
    }

    /// <summary>
    /// BL-BR-23 / BL-BR-27: phụ thu, giảm tay, hoàn trả — lý do bắt buộc, nhập số dương; giảm trừ và hoàn trả lưu số âm.
    /// Giảm trừ không vượt phần thu; hoàn trả được làm tổng phiếu âm (chủ trọ trả lại người thuê).
    /// </summary>
    public Result<InvoiceLine> AddManualLine(
        InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note, Guid? feeTypeId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure<InvoiceLine>(BillingErrors.NotDraft);
        if (!IsManualType(type))
            throw new ArgumentException("Manual lines are surcharges, discounts or refunds.", nameof(type));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure<InvoiceLine>(BillingErrors.NoteRequired);

        var signed = Signed(type, amount);
        if (type != InvoiceLineType.Refund && NetCharges + signed < 0)
            return Result.Failure<InvoiceLine>(BillingErrors.NegativeTotal);

        var line = InvoiceLine.Manual(Id, type, description, quantity, unitPrice, signed, note, feeTypeId,
            PeriodStart, PeriodEnd, 1000 + _lines.Count(l => !l.IsSystem));
        _lines.Add(line);
        Recompute();
        return line;
    }

    public Result UpdateManualLine(Guid lineId, string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId && !l.IsSystem);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(BillingErrors.NoteRequired);

        var signed = Signed(line.Type, amount);
        if (line.Type != InvoiceLineType.Refund && NetCharges - line.Amount + signed < 0)
            return Result.Failure(BillingErrors.NegativeTotal);
        line.UpdateManual(description, quantity, unitPrice, signed, note);
        Recompute();
        return Result.Success();
    }

    public Result RemoveManualLine(Guid lineId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        var line = _lines.FirstOrDefault(l => l.Id == lineId && !l.IsSystem);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (line.Type != InvoiceLineType.Refund && NetCharges - line.Amount < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        _lines.Remove(line);
        Recompute();
        return Result.Success();
    }

    /// <summary>BL-BR-11/13: chốt — hết vấn đề chặn; cấp số, ngày phát hành, hạn thanh toán.</summary>
    public Result Finalize(string invoiceNo, DateOnly today, int paymentDueDays, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (HasBlockingIssues)
            return Result.Failure(BillingErrors.HasIssues(Issues.Where(i => i.Severity == InvoiceIssue.Error).Select(i => i.Code).Distinct().ToList()));
        if (NetCharges < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        InvoiceNo = invoiceNo;
        IssueDate = today;
        DueDate = today.AddDays(paymentDueDays);
        FinalizedAt = now;
        Status = InvoiceStatus.Finalized;
        return Result.Success();
    }

    /// <summary>BL-BR-15: hủy phiếu đã chốt chưa thu tiền — giải phóng đoạn đo để lập lại.</summary>
    public Result Void(string reason, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        if (PaidAmount > 0)
            return Result.Failure(BillingErrors.HasPayments);
        if (RefundedOn is not null)
            return Result.Failure(BillingErrors.RefundConfirmed);

        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedAt = now;
        foreach (var segment in _segments)
            segment.Void();
        return Result.Success();
    }

    /// <summary>M08: cộng / trừ tiền đã thu (PM-BR-04/05) — giữ 0 ≤ đã thu ≤ tổng; <paramref name="writeOff"/> = bỏ nợ (PM-BR-16).</summary>
    public void ApplyPayment(decimal delta, bool writeOff = false)
    {
        if (Status != InvoiceStatus.Finalized)
            throw new InvalidOperationException("Payments apply to finalized invoices only.");
        var paid = PaidAmount + delta;
        if (paid < 0 || paid > TotalAmount)
            throw new InvalidOperationException("Paid amount out of range.");
        PaidAmount = paid;
        if (writeOff)
            WrittenOffAmount += delta;
    }

    /// <summary>BL-BR-27: xác nhận đã trả lại người thuê phần tổng âm của phiếu đã chốt.</summary>
    public Result ConfirmRefund(DateOnly refundedOn, PaymentMethod method, string? note, DateOnly today)
    {
        if (Status != InvoiceStatus.Finalized)
            return Result.Failure(BillingErrors.NotFinalized);
        if (TotalAmount >= 0)
            return Result.Failure(BillingErrors.NothingToRefund);
        if (RefundedOn is not null)
            return Result.Failure(BillingErrors.RefundConfirmed);
        if (refundedOn > today || refundedOn < IssueDate)
            return Result.Failure(BillingErrors.InvalidRefundDate);

        RefundedOn = refundedOn;
        RefundMethod = method;
        RefundNote = TextNormalizer.TrimToNull(note);
        return Result.Success();
    }

    /// <summary>Bỏ xác nhận đã hoàn (nhập nhầm) — cần trước khi hủy phiếu.</summary>
    public Result CancelRefund()
    {
        if (RefundedOn is null)
            return Result.Failure(BillingErrors.RefundNotConfirmed);
        RefundedOn = null;
        RefundMethod = null;
        RefundNote = null;
        return Result.Success();
    }

    public void UpdateNote(string? note) => Note = TextNormalizer.TrimToNull(note);

    /// <summary>BL-BR-14 / RT-BR-06: ngoại lệ duy nhất cho phiếu bất biến — lệnh ẩn danh thay tên người đứng tên đã chụp trên phiếu.</summary>
    public void AnonymizeRepresentative(string anonymizedName) => SnapshotRepresentativeName = anonymizedName;

    public static bool IsManualType(InvoiceLineType type) =>
        type is InvoiceLineType.Surcharge or InvoiceLineType.ManualDiscount or InvoiceLineType.Refund;

    private static decimal Signed(InvoiceLineType type, decimal amount) =>
        type is InvoiceLineType.ManualDiscount or InvoiceLineType.Refund ? -amount : amount;

    private void Recompute()
    {
        Subtotal = _lines.Where(l => l.Amount > 0).Sum(l => l.Amount);
        DiscountTotal = _lines.Where(l => l.Amount < 0 && l.Type != InvoiceLineType.Refund).Sum(l => l.Amount);
        RefundTotal = _lines.Where(l => l.Type == InvoiceLineType.Refund).Sum(l => l.Amount);
        TotalAmount = Subtotal + DiscountTotal + RefundTotal;
    }

    /// <summary>C-03: làm tròn ở cấp dòng, về đồng.</summary>
    public static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}

public sealed class InvoiceLine : TenantEntity
{
    private InvoiceLine() { } // EF Core

    public Guid InvoiceId { get; private set; }
    public InvoiceLineType Type { get; private set; }

    /// <summary>true = hệ thống tính (thay khi tính lại); false = phụ thu / giảm tay.</summary>
    public bool IsSystem { get; private set; }
    public Guid? FeeTypeId { get; private set; }
    public string Description { get; private set; } = null!;
    public string? Unit { get; private set; }
    public DateOnly ServiceFrom { get; private set; }
    public DateOnly ServiceTo { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? ProrationFactor { get; private set; }
    public decimal Amount { get; private set; }
    public bool IsManuallyEdited { get; private set; }
    public decimal? SystemQuantity { get; private set; }
    public decimal? SystemUnitPrice { get; private set; }
    public decimal? SystemAmount { get; private set; }
    public string? Note { get; private set; }
    public int SortOrder { get; private set; }

    public (InvoiceLineType, Guid?) Key => (Type, FeeTypeId);

    internal static InvoiceLine FromCalculation(Guid invoiceId, CalculatedLine line)
    {
        var result = new InvoiceLine { Id = Guid.CreateVersion7(), InvoiceId = invoiceId, IsSystem = true };
        result.Reset(line);
        return result;
    }

    internal static InvoiceLine Manual(
        Guid invoiceId, InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount,
        string note, Guid? feeTypeId, DateOnly from, DateOnly to, int sortOrder) => new()
    {
        Id = Guid.CreateVersion7(),
        InvoiceId = invoiceId,
        Type = type,
        IsSystem = false,
        FeeTypeId = feeTypeId,
        Description = description.Trim(),
        ServiceFrom = from,
        ServiceTo = to,
        Quantity = quantity ?? 1,
        UnitPrice = unitPrice ?? Math.Abs(amount),
        Amount = amount,
        Note = note.Trim(),
        SortOrder = sortOrder
    };

    internal void Reset(CalculatedLine line)
    {
        Type = line.Type;
        FeeTypeId = line.FeeTypeId;
        Description = line.Description;
        Unit = line.Unit;
        ServiceFrom = line.ServiceFrom;
        ServiceTo = line.ServiceTo;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        ProrationFactor = line.ProrationFactor;
        Amount = line.Amount;
        SortOrder = line.SortOrder;
        IsManuallyEdited = false;
        SystemQuantity = SystemUnitPrice = SystemAmount = null;
        Note = null;
    }

    internal void RefreshSystem(CalculatedLine line)
    {
        Description = line.Description;
        Unit = line.Unit;
        ServiceFrom = line.ServiceFrom;
        ServiceTo = line.ServiceTo;
        ProrationFactor = line.ProrationFactor;
        SortOrder = line.SortOrder;
        SystemQuantity = line.Quantity;
        SystemUnitPrice = line.UnitPrice;
        SystemAmount = line.Amount;
    }

    internal void Edit(decimal quantity, decimal unitPrice, decimal amount, string? note)
    {
        if (!IsManuallyEdited)
        {
            SystemQuantity = Quantity;
            SystemUnitPrice = UnitPrice;
            SystemAmount = Amount;
            IsManuallyEdited = true;
        }
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = amount;
        Note = TextNormalizer.TrimToNull(note);
    }

    internal void ClearEdit()
    {
        if (!IsManuallyEdited)
            return;
        Quantity = SystemQuantity!.Value;
        UnitPrice = SystemUnitPrice!.Value;
        Amount = SystemAmount!.Value;
        IsManuallyEdited = false;
        SystemQuantity = SystemUnitPrice = SystemAmount = null;
        Note = null;
    }

    internal void UpdateManual(string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note)
    {
        Description = description.Trim();
        Quantity = quantity ?? 1;
        UnitPrice = unitPrice ?? Math.Abs(amount);
        Amount = amount;
        Note = TextNormalizer.TrimToNull(note);
    }
}

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

    internal void Void() => Voided = true;
}

public static class BillingErrors
{
    public static readonly Error NotFound = Error.NotFound("INVOICE_NOT_FOUND", "Không tìm thấy phiếu tiền phòng.");
    public static readonly Error LineNotFound = Error.NotFound("INVOICE_LINE_NOT_FOUND", "Không tìm thấy dòng phiếu.");
    public static readonly Error NotDraft = Error.BusinessRule("INVOICE_NOT_DRAFT", "Phiếu đã chốt / đã hủy — không sửa được.");
    public static readonly Error NotFinalized = Error.BusinessRule("INVOICE_NOT_FINALIZED", "Phiếu chưa chốt.");
    public static readonly Error NoteRequired = Error.Validation("NOTE_REQUIRED", "Nhập lý do / ghi chú.");
    public static readonly Error NegativeTotal = Error.BusinessRule("NEGATIVE_TOTAL",
        "Giảm trừ không được lớn hơn phần thu — muốn trả lại tiền cho người thuê thì dùng dòng Hoàn trả.");
    public static readonly Error NothingToRefund = Error.BusinessRule("NOTHING_TO_REFUND", "Phiếu không có khoản phải trả lại người thuê.");
    public static readonly Error RefundConfirmed = Error.BusinessRule("REFUND_CONFIRMED",
        "Phiếu đã xác nhận trả lại tiền cho người thuê — bỏ xác nhận trước.");
    public static readonly Error RefundNotConfirmed = Error.BusinessRule("REFUND_NOT_CONFIRMED", "Phiếu chưa xác nhận đã hoàn tiền.");
    public static readonly Error InvalidRefundDate = Error.Validation("INVALID_REFUND_DATE", "Ngày hoàn từ ngày lập phiếu tới hôm nay.");

    public static Error RefundPending(decimal amount) =>
        Error.BusinessRule("REFUND_PENDING", $"Còn {amount:N0}đ phải trả lại người thuê — xác nhận đã hoàn trên phiếu trước.")
            .WithDetail("refundDue", amount);
    public static readonly Error ConcurrencyConflict = Error.Conflict("CONCURRENCY_CONFLICT", "Phiếu vừa được người khác sửa — tải lại.");
    public static readonly Error SaveFailed = Error.Conflict("INVOICE_SAVE_FAILED", "Không lưu được phiếu — tải lại rồi thử lại.");
    public static readonly Error HasPayments = Error.BusinessRule("INVOICE_HAS_PAYMENTS", "Phiếu đã thu tiền — đảo phiếu thu trước khi hủy.");
    public static readonly Error NotLatest = Error.BusinessRule("NOT_LATEST_INVOICE",
        "Chỉ hủy / xóa được phiếu mới nhất của hợp đồng — hủy các phiếu kỳ sau trước.");
    public static readonly Error DraftStale = Error.Conflict("DRAFT_STALE",
        "Dữ liệu nguồn (giá, chỉ số, người ở…) đã đổi sau khi tạo nháp — tính lại rồi chốt.");
    public static readonly Error FinalExists = Error.Conflict("FINAL_INVOICE_EXISTS", "Hợp đồng đã có phiếu quyết toán — mở phiếu đó để sửa.");
    public static readonly Error FinalRequired = Error.BusinessRule("FINAL_INVOICE_REQUIRED", "Lập và chốt phiếu quyết toán trước khi hoàn tất thanh lý.");
    public static readonly Error FinalFinalized = Error.BusinessRule("FINAL_INVOICE_FINALIZED",
        "Phiếu quyết toán đã chốt — hủy phiếu quyết toán trước khi hủy thanh lý.");
    public static readonly Error DraftExists = Error.Conflict("INVOICE_DRAFT_EXISTS", "Hợp đồng còn phiếu nháp — chốt hoặc xóa trước.");
    public static readonly Error PreviousNotBilled = Error.BusinessRule("PREVIOUS_PERIOD_NOT_BILLED", "Chưa lập phiếu kỳ trước — lập lần lượt từng kỳ.");

    public static Error HasDebt(decimal outstanding) =>
        Error.BusinessRule("CONTRACT_HAS_DEBT", $"Hợp đồng còn nợ {outstanding:N0}đ — chọn \"Đã thu toàn bộ\" hoặc \"Bỏ nợ\".")
            .WithDetail("outstanding", outstanding);

    public static readonly Error InvalidBillingMonth = Error.Validation("INVALID_BILLING_MONTH", "Tháng thu dạng yyyy-MM.");

    public static Error HasIssues(IReadOnlyCollection<string> codes) =>
        Error.BusinessRule("INVOICE_HAS_ISSUES", "Phiếu còn vấn đề chưa xử lý (thiếu chỉ số / thiếu giá…).").WithDetail("issues", codes);
}

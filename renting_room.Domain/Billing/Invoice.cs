using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

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

    /// <summary>
    /// BL-BR-29 (H1): "Làm tròn" — bỏ phần lẻ dưới 1.000đ của tổng (về 0: 523.560 ⇒ −560; −12.400 ⇒ +400). Không phải dòng phiếu
    /// (không bị tính lại / sửa tay riêng) — hiển thị thành dòng "Làm tròn" cuối phiếu.
    /// </summary>
    public decimal RoundingAmount { get; private set; }

    /// <summary>Khu bật làm tròn lúc tính phiếu gần nhất — giữ để sửa tay / thêm dòng sau đó vẫn làm tròn.</summary>
    public bool RoundTotal { get; private set; }

    /// <summary>BL-BR-20 (I1): "Cần tính lại" — dữ liệu nguồn đổi sau khi tính nháp; tính lại thì hết. Chỉ gợi ý, chốt vẫn kiểm (BL-BR-12).</summary>
    public bool IsStale { get; private set; }

    /// <summary>Subtotal + DiscountTotal + RefundTotal + RoundingAmount; âm ⇒ chủ trọ phải trả lại người thuê.</summary>
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

    /// <summary>PM-BR-36: thực thu = tiền người thuê trả thật (<see cref="PaidAmount"/> gồm cả phần bỏ nợ).</summary>
    public decimal CollectedAmount => PaidAmount - WrittenOffAmount;

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
        // PM-BR-36: chỉ bỏ nợ một phần mà chưa thu đồng nào thì chưa phải "thu một phần".
        return CollectedAmount > 0 ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
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
        RoundTotal = calculation.RoundTotal;
        IsStale = false;
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
    /// <param name="sourceInvoiceId">BL-BR-27 (E): phiếu đã chốt bị tính sai mà người thuê đã trả — bắt buộc với dòng hoàn trả; số tiền tối đa
    /// (đã thu thật − các lần hoàn trước) do Application kiểm vì cần dữ liệu phiếu khác.</param>
    /// <param name="roomChargeId">Khoản phát sinh của phòng mà dòng này thể hiện (BL-BR-33).</param>
    /// <param name="isSettled">Khoản đã thanh toán / đã hoàn ngay ⇒ dòng hiện trên phiếu nhưng không tính vào tổng.</param>
    public Result<InvoiceLine> AddManualLine(
        InvoiceLineType type, string description, decimal? quantity, decimal? unitPrice, decimal amount, string? note, Guid? feeTypeId,
        Guid? sourceInvoiceId = null, Guid? roomChargeId = null, bool isSettled = false)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure<InvoiceLine>(BillingErrors.NotDraft);
        if (!IsManualType(type))
            throw new ArgumentException("Manual lines are surcharges, discounts or refunds.", nameof(type));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure<InvoiceLine>(BillingErrors.NoteRequired);
        if (type == InvoiceLineType.Refund && (sourceInvoiceId is null || sourceInvoiceId == Id))
            return Result.Failure<InvoiceLine>(BillingErrors.RefundSourceRequired);

        var signed = Signed(type, amount);
        if (type != InvoiceLineType.Refund && !isSettled && NetCharges + signed < 0)
            return Result.Failure<InvoiceLine>(BillingErrors.NegativeTotal);

        var line = InvoiceLine.Manual(Id, type, description, quantity, unitPrice, signed, note, feeTypeId,
            PeriodStart, PeriodEnd, 1000 + _lines.Count(l => !l.IsSystem), type == InvoiceLineType.Refund ? sourceInvoiceId : null);
        line.LinkCharge(roomChargeId, isSettled && type != InvoiceLineType.Refund);
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
        if (line.Type != InvoiceLineType.Refund && !line.IsSettled && NetCharges - line.Amount + signed < 0)
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
        if (line.Type != InvoiceLineType.Refund && !line.IsSettled && NetCharges - line.Amount < 0)
            return Result.Failure(BillingErrors.NegativeTotal);

        _lines.Remove(line);
        Recompute();
        return Result.Success();
    }

    public InvoiceLine? ChargeLine(Guid roomChargeId) => _lines.FirstOrDefault(l => l.RoomChargeId == roomChargeId);

    /// <summary>BL-BR-31: đánh dấu / bỏ đánh dấu "đã thanh toán" của khoản phát sinh đang trên nháp ⇒ dòng ra / vào tổng.</summary>
    public Result SetChargeSettled(Guid roomChargeId, bool settled)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(RoomChargeErrors.Locked);
        var line = ChargeLine(roomChargeId);
        if (line is null)
            return Result.Failure(BillingErrors.LineNotFound);
        if (line.IsSettled == settled)
            return Result.Success();
        // Bỏ đánh dấu một khoản bù / đánh dấu một khoản phụ thu đều có thể làm phần thu âm (BL-BR-10).
        if (NetCharges + (settled ? -line.Amount : line.Amount) < 0)
            return Result.Failure(BillingErrors.NegativeTotal);
        line.LinkCharge(roomChargeId, settled);
        Recompute();
        return Result.Success();
    }

    /// <summary>Hủy khoản phát sinh đang trên nháp ⇒ xóa dòng (BL-BR-35).</summary>
    public Result RemoveChargeLine(Guid roomChargeId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(RoomChargeErrors.Locked);
        return ChargeLine(roomChargeId) is { } line ? RemoveManualLine(line.Id) : Result.Success();
    }

    /// <summary>Cảnh báo thêm sau khi tính (VD khoản bù chưa gắn được — BL-BR-33); không trùng theo mã + tham chiếu.</summary>
    public void AddIssue(InvoiceIssue issue)
    {
        if (!Issues.Any(i => i.Code == issue.Code && i.Ref == issue.Ref))
            Issues = [.. Issues, issue];
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
        // BL-BR-33: dòng khoản phát sinh đã thanh toán / đã hoàn ngay hiện trên phiếu nhưng không tính vào tổng.
        Subtotal = _lines.Where(l => l.Amount > 0 && !l.IsSettled).Sum(l => l.Amount);
        DiscountTotal = _lines.Where(l => l.Amount < 0 && l.Type != InvoiceLineType.Refund && !l.IsSettled).Sum(l => l.Amount);
        RefundTotal = _lines.Where(l => l.Type == InvoiceLineType.Refund).Sum(l => l.Amount);
        var total = Subtotal + DiscountTotal + RefundTotal;
        RoundingAmount = RoundTotal ? -(total % RoundingUnit) : 0;
        TotalAmount = total + RoundingAmount;
    }

    /// <summary>BL-BR-29: làm tròn tổng phiếu tới nghìn đồng.</summary>
    public const decimal RoundingUnit = 1000;

    /// <summary>C-03: làm tròn ở cấp dòng, về đồng.</summary>
    public static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}

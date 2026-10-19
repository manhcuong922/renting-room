using FluentValidation;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

public sealed record InvoiceSummaryDto(
    Guid Id,
    string? InvoiceNo,
    InvoiceType Type,
    InvoiceStatus Status,
    InvoicePaymentStatus? PaymentStatus,
    Guid PropertyId,
    Guid RoomId,
    string RoomCode,
    Guid ContractId,
    string ContractNo,
    string RepresentativeName,
    string BillingMonth,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Outstanding,
    decimal RefundDue,
    DateOnly? DueDate,
    int ErrorCount,
    string Version,
    bool IsStale = false,
    decimal RoundingAmount = 0,
    decimal CollectedAmount = 0,
    decimal WrittenOffAmount = 0);

/// <param name="RoomCode">Phòng của đoạn đo (chuyển phòng ⇒ dòng Điện nước có đoạn của phòng cũ và phòng mới) — UI hiện mỗi đoạn một hàng (BL-BR-05).</param>
/// <param name="StartDate">Ngày ghi chỉ số đầu; <paramref name="EndDate"/> — chỉ số cuối. Các trường bản chụp null ở phiếu lập trước 18/10/2026 mà không tìm lại được.</param>
public sealed record InvoiceSegmentDto(
    Guid MeterId, Guid StartReadingId, Guid EndReadingId, decimal StartValue, decimal EndValue, decimal Consumption,
    Guid? RoomId = null, string? RoomCode = null, string? MeterSerial = null, DateOnly? StartDate = null, DateOnly? EndDate = null);

public sealed record InvoiceLineDto(
    Guid Id,
    InvoiceLineType Type,
    bool IsSystem,
    Guid? FeeTypeId,
    string Description,
    string? Unit,
    DateOnly ServiceFrom,
    DateOnly ServiceTo,
    decimal Quantity,
    decimal UnitPrice,
    decimal? ProrationFactor,
    decimal Amount,
    bool IsManuallyEdited,
    decimal? SystemQuantity,
    decimal? SystemUnitPrice,
    decimal? SystemAmount,
    string? Note,
    IReadOnlyList<InvoiceSegmentDto> Segments,
    Guid? SourceInvoiceId = null,
    Guid? RoomChargeId = null,
    bool IsSettled = false);

/// <summary>F1 (PM-UC-18): phiếu kỳ trước của cùng HĐ còn nợ — chỉ hiển thị, không cộng vào phiếu.</summary>
public sealed record PreviousDebtDto(Guid InvoiceId, string? InvoiceNo, string BillingMonth, DateOnly? DueDate, decimal Outstanding);

/// <summary>PM-BR-31: phiếu còn nợ của HĐ đứng trước trong chuỗi ký lại — chỉ hiển thị, thu tiền ghi trên HĐ đó.</summary>
public sealed record PreviousContractDebtDto(
    Guid ContractId, string ContractNo, Guid InvoiceId, string? InvoiceNo, string BillingMonth, DateOnly? DueDate, decimal Outstanding);

/// <param name="RefundTotal">Σ dòng hoàn trả (≤ 0).</param>
/// <param name="RefundedOn">Ngày chủ trọ xác nhận đã trả lại người thuê phần tổng âm (BL-BR-27).</param>
/// <param name="PreviousDebts">F1: "Nợ các kỳ trước" — chỉ có ở <c>GET /invoices/{id}</c>.</param>
/// <param name="TotalDue">F1: "Tổng cần thanh toán" = còn phải trả của phiếu này (nháp: tổng phiếu) + nợ các kỳ trước.</param>
/// <param name="PreviousContractDebts">Nợ của HĐ trước khi ký lại (PM-BR-31) — danh sách riêng, <b>không</b> cộng vào <paramref name="TotalDue"/>.</param>
public sealed record InvoiceDetailDto(
    InvoiceSummaryDto Summary,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal RefundTotal,
    DateOnly? RefundedOn,
    PaymentMethod? RefundMethod,
    string? RefundNote,
    DateOnly? IssueDate,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<InvoiceIssue> Issues,
    string? Note,
    DateTimeOffset? FinalizedAt,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    IReadOnlyList<PreviousDebtDto>? PreviousDebts = null,
    decimal? TotalDue = null,
    IReadOnlyList<PreviousContractDebtDto>? PreviousContractDebts = null,
    decimal PreviousContractDue = 0);

internal static class InvoiceMapping
{
    public static InvoiceSummaryDto ToSummary(this Invoice i, DateOnly today) => new(
        i.Id, i.InvoiceNo, i.Type, i.Status, i.PaymentStatus(today), i.PropertyId, i.RoomId, i.SnapshotRoomCode, i.ContractId,
        i.SnapshotContractNo, i.SnapshotRepresentativeName, $"{i.BillingMonth:yyyy-MM}", i.PeriodStart, i.PeriodEnd,
        i.TotalAmount, i.PaidAmount, i.Outstanding, i.RefundDue, i.DueDate, i.Issues.Count(x => x.Severity == InvoiceIssue.Error), i.Version.ToString(),
        i.IsStale, i.RoundingAmount, i.CollectedAmount, i.WrittenOffAmount);

    public static InvoiceDetailDto ToDetail(this Invoice i, DateOnly today) => new(
        i.ToSummary(today), i.Subtotal, i.DiscountTotal, i.RefundTotal, i.RefundedOn, i.RefundMethod, i.RefundNote, i.IssueDate,
        i.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.ServiceFrom).Select(l => new InvoiceLineDto(
            l.Id, l.Type, l.IsSystem, l.FeeTypeId, l.Description, l.Unit, l.ServiceFrom, l.ServiceTo, l.Quantity, l.UnitPrice,
            l.ProrationFactor, l.Amount, l.IsManuallyEdited, l.SystemQuantity, l.SystemUnitPrice, l.SystemAmount, l.Note,
            l.Type == InvoiceLineType.Metered && l.IsSystem
                ? i.Segments.Where(s => s.BelongsTo(l)).OrderBy(s => s.StartDate).ThenBy(s => s.StartValue)
                    .Select(s => new InvoiceSegmentDto(s.MeterId, s.StartReadingId, s.EndReadingId, s.StartValue, s.EndValue, s.Consumption,
                        s.RoomId, s.RoomCode, s.MeterSerial, s.StartDate, s.EndDate)).ToList()
                : [], l.SourceInvoiceId, l.RoomChargeId, l.IsSettled)).ToList(),
        i.Issues, i.Note, i.FinalizedAt, i.VoidedAt, i.VoidReason);
}

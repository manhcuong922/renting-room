using System.Globalization;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Exports;

/// <summary>Dữ liệu E2 nạp trong 1 snapshot: hóa đơn (kèm dòng) + tra cứu tên / phiếu bỏ nợ / lịch sử giảm đã thu / ngày thu gần nhất.</summary>
public sealed record InvoiceExportData(
    IReadOnlyList<Invoice> Invoices,
    IReadOnlyDictionary<Guid, string> PropertyNames,
    IReadOnlyDictionary<Guid, string> RoomCodes,
    IReadOnlyDictionary<Guid, string> MeteredSystemCodes,
    IReadOnlyList<InvoiceMoneyNote> WriteOffs,
    IReadOnlyList<InvoiceMoneyNote> Reductions,
    IReadOnlyDictionary<Guid, DateOnly> LastPaidAt,
    IReadOnlyDictionary<Guid, string> UserNames);

/// <summary>Một khoản bỏ nợ / giảm đã thu của một hóa đơn: số tiền, lý do, người làm.</summary>
public sealed record InvoiceMoneyNote(Guid InvoiceId, decimal Amount, string? Reason, Guid? UserId);

internal static class InvoiceExportSheets
{
    private const string Title = "THU TIỀN THEO KỲ";
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private static readonly SpreadsheetColumn[] DetailColumns =
    [
        new("STT", SpreadsheetColumnType.Number, 6),
        new("Kỳ", SpreadsheetColumnType.Text, 9),
        new("Khu", SpreadsheetColumnType.Text, 20),
        new("Phòng", SpreadsheetColumnType.Text, 9),
        new("Số hóa đơn", SpreadsheetColumnType.Text, 15),
        new("Loại", SpreadsheetColumnType.Text, 10),
        new("Người đứng tên", SpreadsheetColumnType.Text, 22),
        new("Trạng thái", SpreadsheetColumnType.Text, 16),
        new("Tiền phòng", SpreadsheetColumnType.Money, 13),
        new("Điện nước", SpreadsheetColumnType.Money, 12),
        new("Dịch vụ", SpreadsheetColumnType.Money, 12),
        new("Phụ thu − giảm trừ − hoàn trả", SpreadsheetColumnType.Money, 14),
        new("Kết toán", SpreadsheetColumnType.Money, 12),
        new("Tổng", SpreadsheetColumnType.Money, 13),
        new("Đã thu", SpreadsheetColumnType.Money, 13),
        new("Ngày thu gần nhất", SpreadsheetColumnType.Date, 12),
        new("Bỏ nợ", SpreadsheetColumnType.Money, 12),
        new("Lý do bỏ nợ", SpreadsheetColumnType.Text, 22),
        new("Người bỏ nợ", SpreadsheetColumnType.Text, 18),
        new("Sửa giảm", SpreadsheetColumnType.Money, 12),
        new("Lý do sửa giảm", SpreadsheetColumnType.Text, 22),
        new("Người sửa", SpreadsheetColumnType.Text, 18),
        new("Còn nợ", SpreadsheetColumnType.Money, 13),
        new("Đơn giá điện", SpreadsheetColumnType.Text, 14),
        new("Đơn giá nước", SpreadsheetColumnType.Text, 14)
    ];

    private static readonly SpreadsheetColumn[] SummaryColumns =
    [
        new("Kỳ", SpreadsheetColumnType.Text, 11),
        new("Khu", SpreadsheetColumnType.Text, 22),
        new("Số hóa đơn", SpreadsheetColumnType.Number, 10),
        new("Tổng", SpreadsheetColumnType.Money, 15),
        new("Đã thu", SpreadsheetColumnType.Money, 15),
        new("Bỏ nợ", SpreadsheetColumnType.Money, 13),
        new("Sửa giảm", SpreadsheetColumnType.Money, 13),
        new("Còn nợ", SpreadsheetColumnType.Money, 15)
    ];

    private sealed record Row(Invoice Invoice, string Property, string Room, decimal Reduced);

    public static IReadOnlyList<SpreadsheetSheet> Build(InvoiceExportData data, DateOnly from, DateOnly to, DateTimeOffset now)
    {
        var today = now.ToBusinessDate();
        var writeOffs = data.WriteOffs.ToLookup(w => w.InvoiceId);
        var reductions = data.Reductions.ToLookup(r => r.InvoiceId);
        var rows = data.Invoices
            .Select(i => new Row(i, data.PropertyNames.GetValueOrDefault(i.PropertyId, string.Empty), data.RoomCodes.GetValueOrDefault(i.RoomId, string.Empty),
                reductions[i.Id].Sum(r => r.Amount)))
            .OrderBy(r => r.Invoice.BillingMonth).ThenBy(r => r.Property, StringComparer.Ordinal).ThenBy(r => r.Room, StringComparer.Ordinal)
            .ThenBy(r => r.Invoice.Type).ThenBy(r => r.Invoice.PeriodStart)
            .ToList();

        var subtitle = $"Kỳ {Month(from)}{(from == to ? string.Empty : $" – {Month(to)}")} · Đã thu = tiền thật thu vào (không gồm bỏ nợ) · Xuất lúc "
            + now.ToOffset(VietnamTime.Offset).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
        var detail = rows.Select((r, index) => Detail(index + 1, r, today, data, writeOffs[r.Invoice.Id].ToList(), reductions[r.Invoice.Id].ToList())).ToList();

        var groups = rows.GroupBy(r => (r.Invoice.BillingMonth, r.Property)).Select(g => Summary(Month(g.Key.BillingMonth), g.Key.Property, g.ToList())).ToList();
        if (rows.Count > 0)
            groups.Add(Summary("Tổng cộng", string.Empty, rows));

        return
        [
            new SpreadsheetSheet("Chi tiết", $"{Title} — CHI TIẾT", subtitle, DetailColumns, detail),
            new SpreadsheetSheet("Tổng theo kỳ", $"{Title} — TỔNG THEO KỲ", subtitle, SummaryColumns, groups)
        ];
    }

    private static object?[] Detail(
        int index, Row r, DateOnly today, InvoiceExportData data, IReadOnlyList<InvoiceMoneyNote> writeOffs, IReadOnlyList<InvoiceMoneyNote> reductions)
    {
        var i = r.Invoice;
        decimal Sum(params InvoiceLineType[] types) => i.Lines.Where(l => types.Contains(l.Type)).Sum(l => l.Amount);
        var rent = Sum(InvoiceLineType.Rent);
        var metered = Sum(InvoiceLineType.Metered);
        var service = Sum(InvoiceLineType.Service);
        var settlement = Sum(InvoiceLineType.PreviousDebt, InvoiceLineType.Deposit);
        var other = i.TotalAmount - rent - metered - service - settlement; // phụ thu, giảm trừ, hoàn trả, làm tròn — cột cộng lại đúng tổng
        var finalized = i.Status == InvoiceStatus.Finalized;
        return
        [
            index,
            Month(i.BillingMonth),
            r.Property,
            r.Room,
            i.InvoiceNo ?? string.Empty,
            i.Type == InvoiceType.Final ? "Trả phòng" : "Tháng",
            i.SnapshotRepresentativeName,
            StatusLabel(i, today),
            rent, metered, service, other, settlement,
            i.TotalAmount,
            finalized ? i.CollectedAmount : 0m,
            data.LastPaidAt.TryGetValue(i.Id, out var paidAt) ? paidAt : null,
            finalized ? i.WrittenOffAmount : 0m,
            Join(writeOffs.Select(w => w.Reason)),
            Join(writeOffs.Select(w => Name(data, w.UserId))),
            r.Reduced,
            Join(reductions.Select(x => x.Reason)),
            Join(reductions.Select(x => Name(data, x.UserId))),
            i.Outstanding,
            UnitPrice(i, data, "ELECTRICITY"),
            UnitPrice(i, data, "WATER")
        ];
    }

    private static object?[] Summary(string month, string property, IReadOnlyList<Row> rows)
    {
        var finalized = rows.Where(r => r.Invoice.Status == InvoiceStatus.Finalized).ToList();
        return
        [
            month, property, rows.Count,
            rows.Sum(r => r.Invoice.TotalAmount),
            finalized.Sum(r => r.Invoice.CollectedAmount),
            finalized.Sum(r => r.Invoice.WrittenOffAmount),
            rows.Sum(r => r.Reduced),
            rows.Sum(r => r.Invoice.Outstanding)
        ];
    }

    /// <summary>Bộ giá trên dòng (M07 BL-BR-39): một giá ⇒ "3.800"; theo bậc ⇒ "bậc 1.984–3.460"; nhiều dòng khác giá ⇒ nối "a / b".</summary>
    private static string UnitPrice(Invoice invoice, InvoiceExportData data, string systemCode)
    {
        var prices = invoice.Lines
            .Where(l => l.Type == InvoiceLineType.Metered && l.FeeTypeId is { } fee && data.MeteredSystemCodes.GetValueOrDefault(fee) == systemCode)
            .Select(l => l.Price?.Tiers is { Count: > 0 } tiers
                ? $"bậc {Money(tiers.Min(t => t.UnitPrice))}–{Money(tiers.Max(t => t.UnitPrice))}"
                : Money(l.Price?.UnitPrice ?? l.UnitPrice))
            .Distinct()
            .ToList();
        return string.Join(" / ", prices);
    }

    private static string StatusLabel(Invoice invoice, DateOnly today) => invoice.Status == InvoiceStatus.Draft ? "Nháp" : invoice.PaymentStatus(today) switch
    {
        InvoicePaymentStatus.Unpaid => "Chưa thu",
        InvoicePaymentStatus.PartiallyPaid => "Thu một phần",
        InvoicePaymentStatus.Paid => "Đã thu đủ",
        InvoicePaymentStatus.Overdue => "Quá hạn",
        InvoicePaymentStatus.WrittenOff => "Đã bỏ nợ",
        InvoicePaymentStatus.RefundPending => "Chờ hoàn tiền",
        InvoicePaymentStatus.Refunded => "Đã hoàn",
        InvoicePaymentStatus.RefundWaived => "Không hoàn",
        InvoicePaymentStatus.CarriedOver or InvoicePaymentStatus.CarriedSettled or InvoicePaymentStatus.CarriedWrittenOff => "Chuyển vào hóa đơn trả phòng",
        _ => string.Empty
    };

    private static string Name(InvoiceExportData data, Guid? userId) => userId is { } id ? data.UserNames.GetValueOrDefault(id, string.Empty) : string.Empty;

    private static string Join(IEnumerable<string?> values) =>
        string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct());

    private static string Money(decimal value) => value.ToString("N0", Vi);

    private static string Month(DateOnly month) => month.ToString("MM/yyyy", CultureInfo.InvariantCulture);
}

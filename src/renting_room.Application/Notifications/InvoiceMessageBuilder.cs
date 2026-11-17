using System.Globalization;
using System.Text;
using renting_room.Domain.Billing;
using renting_room.Domain.Notifications;
using renting_room.Domain.Properties;

namespace renting_room.Application.Notifications;

public sealed record MessageContent(string Subject, string Text);

/// <summary>
/// M11 §7 / NT-BR-17: dựng nội dung tin từ phiếu — văn bản thuần. KHÔNG có số giấy tờ, ngày sinh, số điện thoại người thuê; chỉ có tên khu,
/// mã phòng, tên người đứng tên, tháng thu, các dòng tiền, tổng, còn nợ, hạn nộp, tài khoản ngân hàng của khu.
/// </summary>
public static class InvoiceMessageBuilder
{
    /// <summary>Zalo nhận tối đa 2000 ký tự.</summary>
    public const int MaxLength = 2000;

    public const string UnsubscribeHint = "(Nhắn HUY để ngừng nhận tin.)";

    public static MessageContent Build(
        Invoice invoice, string propertyName, BankAccount? bank, NotificationKind kind, int step, DateOnly today, bool includeUnsubscribeHint = true)
    {
        var month = $"{invoice.BillingMonth.Month}/{invoice.BillingMonth.Year}";
        var title = $"Phiếu tiền phòng {invoice.SnapshotRoomCode} – tháng {month}";
        var lines = invoice.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.ServiceFrom).Select(Describe).ToList();

        var text = Compose(invoice, propertyName, bank, kind, step, today, title, lines, includeUnsubscribeHint);
        if (text.Length > MaxLength && lines.Count > 1)
        {
            // Quá dài ⇒ giữ dòng đầu (tiền phòng), gộp phần còn lại thành "Các khoản khác".
            var rest = invoice.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.ServiceFrom).Skip(1).Sum(l => l.Amount);
            text = Compose(invoice, propertyName, bank, kind, step, today, title, [lines[0], $"- Các khoản khác: {Money(rest)}"], includeUnsubscribeHint);
        }
        return new MessageContent(kind == NotificationKind.OverdueReminder ? $"Nhắc nợ: {title}" : title, text);
    }

    private static string Compose(
        Invoice invoice, string propertyName, BankAccount? bank, NotificationKind kind, int step, DateOnly today, string title,
        IReadOnlyList<string> lines, bool includeUnsubscribeHint)
    {
        var text = new StringBuilder();
        text.Append('[').Append(propertyName).Append("] ").AppendLine(title);
        if (kind == NotificationKind.OverdueReminder && invoice.DueDate is { } due)
            text.AppendLine($"Phiếu đã quá hạn {Math.Max(today.DayNumber - due.DayNumber, step)} ngày, còn nợ {Money(invoice.Outstanding)}.");
        else if (kind == NotificationKind.ManualResend && invoice.IsAdjusted)
            text.AppendLine("Hóa đơn đã điều chỉnh: số liệu dưới đây là bản mới nhất.");
        text.AppendLine($"Người thuê: {invoice.SnapshotRepresentativeName}");
        foreach (var line in lines)
            text.AppendLine(line);
        text.AppendLine($"TỔNG: {Money(invoice.TotalAmount)}");
        if (invoice.PaidAmount > 0)
            text.AppendLine($"Đã thu: {Money(invoice.PaidAmount)} – Còn nợ: {Money(invoice.Outstanding)}");
        if (invoice.DueDate is { } dueDate)
            text.AppendLine($"Hạn nộp: {dueDate:dd/MM/yyyy}");
        if (bank is not null)
        {
            text.AppendLine($"Chuyển khoản: {bank.BankName} – {bank.AccountNo} – {bank.AccountName}");
            text.AppendLine($"Nội dung CK: {invoice.SnapshotRoomCode} T{invoice.BillingMonth.Month:00}");
        }
        if (includeUnsubscribeHint)
            text.Append(UnsubscribeHint);
        return text.ToString().TrimEnd();
    }

    private static string Describe(InvoiceLine line)
    {
        var quantity = line.Unit is { Length: > 0 } unit && line.Type == InvoiceLineType.Metered
            ? $" ({line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)} {unit})"
            : string.Empty;
        return $"- {line.Description}{quantity}: {Money(line.Amount)}";
    }

    /// <summary>3.110.000đ — dấu chấm phân nhóm, không phụ thuộc ICU / văn hóa máy chủ (C-03).</summary>
    public static string Money(decimal amount) =>
        amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.') + "đ";
}

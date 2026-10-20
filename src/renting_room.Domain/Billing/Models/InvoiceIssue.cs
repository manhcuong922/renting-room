namespace renting_room.Domain.Billing;

/// <param name="Amount">Số tiền gợi ý kèm cảnh báo (VD <c>RENT_OVERPAID</c>: số nên hoàn, <paramref name="Ref"/> = hóa đơn nguồn).</param>
public sealed record InvoiceIssue(string Code, string Severity, string Message, Guid? Ref = null, decimal? Amount = null)
{
    public const string Error = "Error";
    public const string Warning = "Warning";
}

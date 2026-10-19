namespace renting_room.Domain.Billing;

/// <summary>Vấn đề của phiếu nháp: <c>Error</c> chặn chốt (thiếu chỉ số, thiếu giá…), <c>Warning</c> chỉ nhắc.</summary>
public sealed record InvoiceIssue(string Code, string Severity, string Message, Guid? Ref = null)
{
    public const string Error = "Error";
    public const string Warning = "Warning";
}

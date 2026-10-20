namespace renting_room.Domain.Billing;

public sealed record InvoiceIssue(string Code, string Severity, string Message, Guid? Ref = null)
{
    public const string Error = "Error";
    public const string Warning = "Warning";
}

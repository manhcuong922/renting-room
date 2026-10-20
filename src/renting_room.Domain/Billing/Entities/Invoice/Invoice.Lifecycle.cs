using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public sealed partial class Invoice
{
    public Result Finalize(string invoiceNo, DateOnly today, int paymentDueDays, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(BillingErrors.NotDraft);
        if (HasBlockingIssues)
            return Result.Failure(BillingErrors.HasIssues(Issues.Where(i => i.Severity == InvoiceIssue.Error).Select(i => i.Code).Distinct().ToList()));
        if (RejectsNegative(NetCharges))
            return Result.Failure(BillingErrors.NegativeTotal);

        InvoiceNo = invoiceNo;
        IssueDate = today;
        DueDate = today.AddDays(paymentDueDays);
        FinalizedAt = now;
        Status = InvoiceStatus.Finalized;
        return Result.Success();
    }

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

    public void UpdateNote(string? note) => Note = TextNormalizer.TrimToNull(note);

    public void AnonymizeRepresentative(string anonymizedName) => SnapshotRepresentativeName = anonymizedName;
}

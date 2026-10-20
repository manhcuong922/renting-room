using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

public sealed partial class Invoice
{
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
        if (RejectsNegative(NetCharges))
            Issues = [.. Issues, new InvoiceIssue("NEGATIVE_TOTAL", InvoiceIssue.Error, "Giảm trừ lớn hơn phần thu — giảm bớt dòng giảm trừ.")];
        return Result.Success();
    }

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

    public void AddIssue(InvoiceIssue issue)
    {
        if (!Issues.Any(i => i.Code == issue.Code && i.Ref == issue.Ref))
            Issues = [.. Issues, issue];
    }
}

using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

// Tính phiếu nháp (BL-BR-07, BL-BR-12) và cảnh báo trên phiếu
public sealed partial class Invoice
{
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

    /// <summary>Cảnh báo thêm sau khi tính (VD khoản bù chưa gắn được — BL-BR-33); không trùng theo mã + tham chiếu.</summary>
    public void AddIssue(InvoiceIssue issue)
    {
        if (!Issues.Any(i => i.Code == issue.Code && i.Ref == issue.Ref))
            Issues = [.. Issues, issue];
    }
}

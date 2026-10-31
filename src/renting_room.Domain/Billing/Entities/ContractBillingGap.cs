using renting_room.Domain.Common;

namespace renting_room.Domain.Billing;

/// <summary>
/// Khoảng kỳ liền nhau của 1 HĐ không tính tiền qua hệ thống (đã thu tay ngoài) — M07 BL-BR-42. Không có phiếu nào cho các kỳ trong khoảng;
/// BL-BR-21 coi kỳ liền trước kỳ sau khoảng là kỳ trước khoảng.
/// </summary>
public sealed class ContractBillingGap : TenantEntity
{
    public const int MaxPeriods = 12;

    private ContractBillingGap() { } // EF Core

    public Guid ContractId { get; private set; }

    /// <summary>Đầu kỳ đầu tiên bị bỏ.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>Cuối kỳ cuối bị bỏ.</summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>Đầu kỳ cuối bị bỏ — chỉ số <c>Periodic</c> chốt kỳ này là chỉ số đầu của kỳ sau khoảng (BL-BR-44).</summary>
    public DateOnly ClosingPeriodStart { get; private set; }

    public int PeriodCount { get; private set; }

    /// <summary>Phiếu đầu tiên sau khoảng; null khi khoảng đóng bởi thanh lý "đã xử lý ngoài hệ thống" (BL-BR-46).</summary>
    public Guid? ResumedInvoiceId { get; private set; }

    public string? Note { get; private set; }

    public static ContractBillingGap Create(
        Guid contractId, IReadOnlyList<BillingPeriod> skipped, Guid? resumedInvoiceId, string? note)
    {
        if (skipped.Count is 0 or > MaxPeriods)
            throw new ArgumentOutOfRangeException(nameof(skipped));
        return new ContractBillingGap
        {
            Id = Guid.CreateVersion7(),
            ContractId = contractId,
            FromDate = skipped[0].Start,
            ToDate = skipped[^1].End,
            ClosingPeriodStart = skipped[^1].Start,
            PeriodCount = skipped.Count,
            ResumedInvoiceId = resumedInvoiceId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }
}

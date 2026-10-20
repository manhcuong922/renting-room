using renting_room.Application.Common.Models;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

public interface IInvoiceQueries
{
    Task<Invoice?> GetDetailsAsync(Guid id, CancellationToken cancellationToken);

    Task<Invoice?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<Invoice>> ListAsync(ListInvoicesQuery request, CancellationToken cancellationToken);

    Task<InvoiceTotalsDto> GetTotalsAsync(ListInvoicesQuery filter, DateOnly today, CancellationToken cancellationToken);

    Task<decimal> GetContractOutstandingAsync(Guid contractId, CancellationToken cancellationToken);

    Task<decimal> GetRefundDueAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Phần còn nợ của phiếu đã chốt chưa thu đủ; null = không có phiếu như vậy.</summary>
    Task<decimal?> GetInvoiceOutstandingAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<Guid?> GetContractIdAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Trạng thái phiếu của HĐ; null = HĐ không có phiếu này.</summary>
    Task<InvoiceStatus?> GetStatusAsync(Guid invoiceId, Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string?>> GetNumbersAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, InvoiceRef>> GetRefsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceBalanceRow>> ListForBalanceAsync(Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoicePeriodRow>> ListPeriodsAsync(Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreviousDebtDto>> ListPreviousDebtsAsync(
        Guid contractId, Guid excludeInvoiceId, DateOnly periodStart, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContractDebtRow>> ListOpenDebtsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    Task<bool> HasLaterInvoiceAsync(Invoice invoice, CancellationToken cancellationToken);

    Task<bool> HasFinalizedFinalAsync(Guid contractId, CancellationToken cancellationToken);

    Task<bool> HasDraftAsync(Guid contractId, CancellationToken cancellationToken);

    Task<bool> HasInvoiceAfterAsync(Guid contractId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Đã lập phiếu (chưa hủy) cho phòng của HĐ có kỳ kết thúc từ ngày <paramref name="date"/>.</summary>
    Task<bool> HasInvoiceForRoomFromAsync(Guid contractId, Guid roomId, DateOnly date, CancellationToken cancellationToken);

    Task<List<Guid>> ListSegmentEndReadingIdsAsync(Guid contractId, IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    Task<Guid?> FindDraftIdForChargeAsync(Guid contractId, DateOnly incurredOn, CancellationToken cancellationToken);

    /// <summary>Tổng các dòng hoàn trả (phiếu chưa hủy) trỏ tới phiếu nguồn, trừ dòng <paramref name="excludeLineId"/> (≤ 0).</summary>
    Task<decimal> GetRefundLinesTotalAsync(Guid sourceInvoiceId, Guid? excludeLineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceTarget>> ListRecalculationTargetsAsync(RecalculateInvoicesCommand request, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceTarget>> ListManualLineTargetsAsync(BulkAddManualLineCommand request, CancellationToken cancellationToken);

    Task<InvoiceSourceData> LoadSourceDataAsync(
        Contract contract, DateOnly periodStart, Guid? excludeInvoiceId, bool isFinal, CancellationToken cancellationToken);

    Task<HashSet<Guid>> GetLockedReadingIdsAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    Task<Dictionary<Guid, DateOnly>> GetMeterLockedUntilAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    Task<SegmentRef?> FindFinalizedSegmentEndingAtAsync(Guid readingId, CancellationToken cancellationToken);

    Task<bool> HasFinalizedSegmentStartingAtAsync(Guid readingId, CancellationToken cancellationToken);

    Task<InvoiceLine> GetSystemMeteredLineAsync(Guid invoiceId, Guid feeTypeId, DateOnly? usageStart, CancellationToken cancellationToken);

    /// <summary>Nháp đang dùng các công tơ này: nháp của HĐ ở phòng có công tơ, hoặc có đoạn đo trên công tơ.</summary>
    Task<IReadOnlyList<DraftRef>> ListDraftsUsingMetersAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    /// <summary>Hóa đơn thường chưa hủy của các HĐ (lưới ghi chỉ số — M06 §3.4).</summary>
    Task<IReadOnlyList<SheetInvoiceRow>> ListSheetInvoicesAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceAdjustment>> ListAdjustmentsAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SegmentEndRow>> ListSegmentEndsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeteredUsageRow>> ListMeteredUsageAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);
}

public sealed record InvoiceBalanceRow(
    Guid Id, string? InvoiceNo, DateOnly BillingMonth, InvoiceType Type, InvoiceStatus Status, decimal TotalAmount, decimal PaidAmount,
    decimal WrittenOffAmount, DateOnly? DueDate, DateOnly PeriodStart);

public sealed record InvoicePeriodRow(InvoiceType Type, InvoiceStatus Status, DateOnly PeriodStart, DateOnly PeriodEnd);

public sealed record InvoiceRef(Guid Id, string? InvoiceNo, InvoiceStatus Status, DateOnly BillingMonth);

public sealed record InvoiceTarget(Guid Id, Guid ContractId, Guid RoomId, string RoomCode, InvoiceStatus Status);

public sealed record ContractDebtRow(Guid ContractId, Guid InvoiceId, string? InvoiceNo, DateOnly BillingMonth, DateOnly? DueDate, decimal Outstanding);

public sealed record DraftRef(Guid InvoiceId, Guid ContractId);

public sealed record SheetInvoiceRow(Guid Id, Guid ContractId, DateOnly PeriodStart, InvoiceStatus Status, bool IsStale);

public sealed record SegmentRef(Guid InvoiceId, Guid ContractId, Guid FeeTypeId, DateOnly? UsageStart);

public sealed record SegmentEndRow(Guid ContractId, DateOnly PeriodStart, Guid MeterId, Guid EndReadingId);

public sealed record MeteredUsageRow(Guid ContractId, Guid FeeTypeId, DateOnly PeriodStart, decimal Quantity);

public sealed record InvoiceSourceData(
    IReadOnlyList<Meter> Meters, IReadOnlyList<(Guid MeterId, Guid EndReadingId, DateOnly PeriodStart)> PreviousSegments,
    IReadOnlyList<decimal>? FinalRentAmounts, bool HasRegularInFinalPeriod, IReadOnlyList<(Guid FeeTypeId, DateOnly PeriodStart, decimal Quantity)> RecentUsage,
    HashSet<Guid> ClosedReadingIds, IReadOnlyDictionary<Guid, string> RoomCodes);

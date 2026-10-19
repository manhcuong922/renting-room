using renting_room.Application.Common.Models;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Billing;

/// <summary>Đọc phiếu (chỉ đọc, không dùng để sửa).</summary>
public interface IInvoiceQueries
{
    /// <summary>Phiếu kèm dòng và đoạn đo.</summary>
    Task<Invoice?> GetDetailsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Phiếu (không kèm dòng).</summary>
    Task<Invoice?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<Invoice>> ListAsync(ListInvoicesQuery request, CancellationToken cancellationToken);

    /// <summary>BL-UC-02: số tổng hợp trên toàn bộ kết quả lọc.</summary>
    Task<InvoiceTotalsDto> GetTotalsAsync(ListInvoicesQuery filter, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Còn nợ của HĐ — chỉ phiếu đã chốt chưa thu đủ; phiếu tổng âm (phải hoàn) không bù trừ nợ.</summary>
    Task<decimal> GetContractOutstandingAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Số chủ trọ còn phải trả lại người thuê (phiếu đã chốt tổng âm, chưa xác nhận đã hoàn — BL-BR-27).</summary>
    Task<decimal> GetRefundDueAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Phần còn nợ của phiếu đã chốt chưa thu đủ; null = không có phiếu như vậy.</summary>
    Task<decimal?> GetInvoiceOutstandingAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<Guid?> GetContractIdAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Trạng thái phiếu của HĐ; null = HĐ không có phiếu này.</summary>
    Task<InvoiceStatus?> GetStatusAsync(Guid invoiceId, Guid contractId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string?>> GetNumbersAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, InvoiceRef>> GetRefsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    /// <summary>Phiếu chưa hủy của HĐ, theo kỳ.</summary>
    Task<IReadOnlyList<InvoiceBalanceRow>> ListForBalanceAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Phiếu chưa hủy của HĐ (loại, trạng thái, kỳ).</summary>
    Task<IReadOnlyList<InvoicePeriodRow>> ListPeriodsAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>F1 (PM-UC-18): phiếu đã chốt còn nợ của HĐ, kỳ trước <paramref name="periodStart"/> — hạn sớm nhất trước.</summary>
    Task<IReadOnlyList<PreviousDebtDto>> ListPreviousDebtsAsync(
        Guid contractId, Guid excludeInvoiceId, DateOnly periodStart, CancellationToken cancellationToken);

    /// <summary>Phiếu đã chốt còn nợ của các HĐ — hạn sớm nhất trước.</summary>
    Task<IReadOnlyList<ContractDebtRow>> ListOpenDebtsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    /// <summary>
    /// BL-BR-22: đã có phiếu chưa hủy mới hơn của HĐ. Phiếu quyết toán cùng kỳ với phiếu thường của kỳ cuối nhưng luôn là mới nhất.
    /// </summary>
    Task<bool> HasLaterInvoiceAsync(Invoice invoice, CancellationToken cancellationToken);

    Task<bool> HasFinalizedFinalAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>HĐ còn phiếu nháp (chưa hủy).</summary>
    Task<bool> HasDraftAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>CT-BR-11: còn phiếu (chưa hủy) của kỳ bắt đầu sau <paramref name="date"/>.</summary>
    Task<bool> HasInvoiceAfterAsync(Guid contractId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Đã lập phiếu (chưa hủy) cho phòng của HĐ có kỳ kết thúc từ ngày <paramref name="date"/>.</summary>
    Task<bool> HasInvoiceForRoomFromAsync(Guid contractId, Guid roomId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Chỉ số cuối của các đoạn đo (chưa bỏ, phiếu chưa hủy) của HĐ trên các công tơ.</summary>
    Task<List<Guid>> ListSegmentEndReadingIdsAsync(Guid contractId, IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    /// <summary>Nháp của HĐ nhận được khoản phát sinh ngày <paramref name="incurredOn"/> (BL-BR-32).</summary>
    Task<Guid?> FindDraftIdForChargeAsync(Guid contractId, DateOnly incurredOn, CancellationToken cancellationToken);

    /// <summary>Tổng các dòng hoàn trả (phiếu chưa hủy) trỏ tới phiếu nguồn, trừ dòng <paramref name="excludeLineId"/> (≤ 0).</summary>
    Task<decimal> GetRefundLinesTotalAsync(Guid sourceInvoiceId, Guid? excludeLineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceTarget>> ListRecalculationTargetsAsync(RecalculateInvoicesCommand request, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceTarget>> ListManualLineTargetsAsync(BulkAddManualLineCommand request, CancellationToken cancellationToken);

    /// <summary>Dữ liệu nguồn của bộ tính phiếu (trừ cài đặt kỳ thu và khoản thu).</summary>
    Task<InvoiceSourceData> LoadSourceDataAsync(
        Contract contract, DateOnly periodStart, Guid? excludeInvoiceId, bool isFinal, CancellationToken cancellationToken);

    /// <summary>MT-BR-06: chỉ số là đầu / cuối của đoạn đo trên phiếu đã chốt hoặc của khoản chốt điện nước khi chuyển phòng chưa hủy.</summary>
    Task<HashSet<Guid>> GetLockedReadingIdsAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    /// <summary>MT-BR-09/10: ngày cuối kỳ sử dụng muộn nhất đã chốt của từng công tơ.</summary>
    Task<Dictionary<Guid, DateOnly>> GetMeterLockedUntilAsync(IReadOnlyCollection<Guid> meterIds, CancellationToken cancellationToken);

    /// <summary>Đoạn đo (phiếu đã chốt, chưa bỏ) kết thúc ở chỉ số này.</summary>
    Task<SegmentRef?> FindFinalizedSegmentEndingAtAsync(Guid readingId, CancellationToken cancellationToken);

    Task<bool> HasFinalizedSegmentStartingAtAsync(Guid readingId, CancellationToken cancellationToken);

    /// <summary>Dòng điện nước hệ thống của phiếu cho khoản thu (và kỳ sử dụng, nếu có).</summary>
    Task<InvoiceLine> GetSystemMeteredLineAsync(Guid invoiceId, Guid feeTypeId, DateOnly? usageStart, CancellationToken cancellationToken);

    /// <summary>Chỉ số cuối các đoạn đo trên phiếu chưa hủy của các HĐ.</summary>
    Task<IReadOnlyList<SegmentEndRow>> ListSegmentEndsAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);

    /// <summary>Sản lượng điện nước trên phiếu thường chưa hủy của các HĐ.</summary>
    Task<IReadOnlyList<MeteredUsageRow>> ListMeteredUsageAsync(IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken);
}

public sealed record InvoiceBalanceRow(
    Guid Id, string? InvoiceNo, DateOnly BillingMonth, InvoiceType Type, InvoiceStatus Status, decimal TotalAmount, decimal PaidAmount,
    decimal WrittenOffAmount, DateOnly? DueDate, DateOnly PeriodStart);

public sealed record InvoicePeriodRow(InvoiceType Type, InvoiceStatus Status, DateOnly PeriodStart, DateOnly PeriodEnd);

public sealed record InvoiceRef(Guid Id, string? InvoiceNo, InvoiceStatus Status, DateOnly BillingMonth);

public sealed record InvoiceTarget(Guid Id, Guid ContractId, Guid RoomId, string RoomCode, InvoiceStatus Status);

public sealed record ContractDebtRow(Guid ContractId, Guid InvoiceId, string? InvoiceNo, DateOnly BillingMonth, DateOnly? DueDate, decimal Outstanding);

public sealed record SegmentRef(Guid InvoiceId, Guid ContractId, Guid FeeTypeId, DateOnly? UsageStart);

public sealed record SegmentEndRow(Guid ContractId, DateOnly PeriodStart, Guid MeterId, Guid EndReadingId);

public sealed record MeteredUsageRow(Guid ContractId, Guid FeeTypeId, DateOnly PeriodStart, decimal Quantity);

/// <param name="PreviousSegments">Đoạn đo trên phiếu chưa hủy kỳ trước (phiếu quyết toán: cả kỳ cuối).</param>
/// <param name="FinalRentAmounts">Phiếu quyết toán: tiền phòng đã lên phiếu khác của kỳ cuối; null nếu không phải quyết toán.</param>
/// <param name="RecentUsage">Sản lượng điện nước các phiếu thường trước kỳ.</param>
/// <param name="ClosedReadingIds">BL-BR-38: chỉ số cuối đã thu bằng khoản chốt điện nước khi chuyển phòng.</param>
public sealed record InvoiceSourceData(
    IReadOnlyList<Meter> Meters, IReadOnlyList<(Guid MeterId, Guid EndReadingId, DateOnly PeriodStart)> PreviousSegments,
    IReadOnlyList<decimal>? FinalRentAmounts, bool HasRegularInFinalPeriod, IReadOnlyList<(Guid FeeTypeId, DateOnly PeriodStart, decimal Quantity)> RecentUsage,
    HashSet<Guid> ClosedReadingIds, IReadOnlyDictionary<Guid, string> RoomCodes);

public interface IRoomChargeQueries
{
    /// <summary>Khoản phát sinh chưa hủy, chưa lên phiếu của HĐ.</summary>
    Task<int> CountPendingAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Mới nhất trước, tối đa <paramref name="maxRows"/>.</summary>
    Task<IReadOnlyList<RoomCharge>> ListAsync(ListRoomChargesQuery request, int maxRows, CancellationToken cancellationToken);

    Task<Guid?> GetContractIdAsync(Guid chargeId, CancellationToken cancellationToken);

    /// <summary>BL-BR-38: khoản chốt điện nước khi chuyển phòng (gắn công tơ).</summary>
    Task<bool> IsMeterClosingAsync(Guid chargeId, CancellationToken cancellationToken);

    /// <summary>Khoản bù chưa hủy, chưa trả, chưa vào phiếu đã chốt của HĐ.</summary>
    Task<decimal> GetWaitingCreditTotalAsync(Guid contractId, CancellationToken cancellationToken);

    /// <summary>Khoản bù đã đánh dấu trả, chưa hủy.</summary>
    Task<SettledCredit?> FindSettledCreditAsync(Guid chargeId, CancellationToken cancellationToken);
}

public sealed record SettledCredit(Guid ContractId, DateOnly IncurredOn, decimal Amount);

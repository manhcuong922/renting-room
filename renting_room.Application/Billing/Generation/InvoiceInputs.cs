using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

internal static class BillingMonths
{
    /// <summary>"yyyy-MM" → ngày 1 của tháng.</summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ? month : null;

    public static IRuleBuilderOptions<T, string?> BillingMonth<T>(this IRuleBuilder<T, string?> rule, TimeProvider clock) =>
        rule.Must(v => Parse(v) is { } m && m <= clock.GetUtcNow().ToBusinessDate().AddMonths(1))
            .WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng thu dạng yyyy-MM, tối đa tháng sau.");

    /// <summary>Kỳ của HĐ có tháng thu = <paramref name="month"/> (C-05: tháng của kỳ chuẩn của khu — mỗi HĐ mỗi tháng tối đa 1 kỳ).</summary>
    public static BillingPeriod? PeriodIn(Contract contract, DateOnly month, BillingSchedule schedule) =>
        contract.BillingPeriods(schedule, month.AddMonths(2)).FirstOrDefault(p => p.Month == month);
}

internal static class InvoiceInputs
{
    /// <summary>
    /// Dữ liệu nguồn cho <see cref="InvoiceCalculator"/>: dịch vụ + điện nước có công tơ ở phòng (kèm giá), công tơ của phòng (kèm chỉ số),
    /// chỉ số cuối của đoạn đo gần nhất trên phiếu chưa hủy kỳ trước (chuỗi liên tục MT-BR-12).
    /// </summary>
    /// <param name="type"><c>Final</c> ⇒ kèm bối cảnh quyết toán (tiền phòng kỳ cuối đã thu chưa, kỳ cuối đã có phiếu thường chưa) và
    /// nối chuỗi chỉ số cả với phiếu thường của chính kỳ cuối.</param>
    public static async Task<InvoiceCalcInput> LoadAsync(
        IAppDbContext db, Contract contract, BillingPeriod period, Guid? excludeInvoiceId, CancellationToken ct,
        InvoiceType type = InvoiceType.Regular)
    {
        var isFinal = type == InvoiceType.Final;
        var billing = await PropertyBilling.LoadAsync(db, contract.PropertyId, ct);
        var types = await ContractFeeRules.LoadTypesAsync(db, contract, ct);
        // CT-BR-47: công tơ của mọi phòng HĐ đã ở (chuyển phòng) — bộ tính chỉ lấy phòng có khoảng ở trùng kỳ sử dụng.
        var roomIds = contract.RoomStays.Select(s => s.RoomId).Distinct().ToList();
        var meters = await db.Meters.AsNoTracking().Include(m => m.Readings).Where(m => roomIds.Contains(m.RoomId)).ToListAsync(ct);
        var others = db.Invoices.AsNoTracking()
            .Where(i => i.ContractId == contract.Id && i.Status != InvoiceStatus.Void && (excludeInvoiceId == null || i.Id != excludeInvoiceId));
        var previous = await others.Where(i => isFinal ? i.PeriodStart <= period.Start : i.PeriodStart < period.Start)
            .SelectMany(i => i.Segments.Select(s => new { s.MeterId, s.EndReadingId, i.PeriodStart }))
            .ToListAsync(ct);
        var lastEnd = previous.GroupBy(s => s.MeterId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.PeriodStart)!.EndReadingId);

        FinalSettlement? final = null;
        if (isFinal)
        {
            var rentLines = await others.SelectMany(i => i.Lines)
                .Where(l => l.Type == InvoiceLineType.Rent && l.ServiceFrom <= period.Start && l.ServiceTo >= period.Start)
                .Select(l => l.Amount).ToListAsync(ct);
            var hasRegular = await others.AnyAsync(i => i.Type == InvoiceType.Regular && i.PeriodStart == period.Start, ct);
            final = new FinalSettlement(rentLines.Count > 0 ? rentLines.Sum() : null, hasRegular);
        }
        // MT-BR-08: sản lượng điện nước các phiếu thường trước (mới nhất trước) để cảnh báo bất thường.
        var recent = (await others.Where(i => i.Type == InvoiceType.Regular && i.PeriodStart < period.Start)
                .SelectMany(i => i.Lines.Where(l => l.Type == InvoiceLineType.Metered && l.FeeTypeId != null)
                    .Select(l => new { FeeTypeId = l.FeeTypeId!.Value, i.PeriodStart, l.Quantity }))
                .ToListAsync(ct))
            .GroupBy(x => x.FeeTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<decimal>)g.OrderByDescending(x => x.PeriodStart).Select(x => x.Quantity).ToList());
        // BL-BR-38 (dữ liệu trước 17/10/2026): đoạn đo đã thu bằng khoản chốt điện nước khi chuyển phòng — phiếu không tính lại.
        var closed = (await db.RoomCharges.AsNoTracking()
                .Where(c => c.ContractId == contract.Id && c.CancelledAt == null && c.EndReadingId != null)
                .Select(c => c.EndReadingId!.Value).ToListAsync(ct))
            .ToHashSet();
        var roomCodes = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, ct);
        return new InvoiceCalcInput(contract, period, billing.Schedule, billing.ProrationMode, types, meters, lastEnd, final, recent,
            billing.RoundInvoiceTotal, closed, roomCodes);
    }

    /// <summary>Kỳ hiện tại của phiếu theo HĐ (có thể bị cắt ngắn nếu HĐ đã bắt đầu thanh lý).</summary>
    public static BillingPeriod? CurrentPeriod(Contract contract, Invoice invoice, BillingSchedule schedule) =>
        contract.BillingPeriods(schedule, invoice.PeriodStart).FirstOrDefault(p => p.Start == invoice.PeriodStart);
}

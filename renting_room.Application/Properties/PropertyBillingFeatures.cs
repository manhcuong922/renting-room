using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// ============================================================ Cài đặt kỳ thu của khu (PR-BR-09, K4 — BL-BR-28)

internal static class BillingChangePlanner
{
    /// <summary>
    /// Đầu kỳ chuẩn đầu tiên chưa lập phiếu của khu = ngày sau kỳ chuẩn chứa phiếu (chưa hủy) muộn nhất. Null = khu chưa có phiếu ⇒ đổi cài đặt
    /// áp lại từ đầu, không cần kỳ chuyển tiếp.
    /// </summary>
    public static async Task<DateOnly?> FirstUnbilledPeriodStartAsync(IAppDbContext db, Property property, CancellationToken ct)
    {
        var lastEnd = await db.Invoices.Where(i => i.PropertyId == property.Id && i.Status != InvoiceStatus.Void)
            .MaxAsync(i => (DateOnly?)i.PeriodEnd, ct);
        return lastEnd is { } end ? property.BillingSchedule.StandardPeriodContaining(end).End.AddDays(1) : null;
    }
}

public sealed record TransitionRoomDto(Guid ContractId, string RoomCode, decimal MonthlyRent, decimal PerDay, decimal SuggestedAmount);

/// <param name="EffectiveFrom">Đầu kỳ chuyển tiếp (null = khu chưa có phiếu ⇒ áp lại từ đầu, không có kỳ chuyển tiếp).</param>
/// <param name="DeviationDays">Kỳ chuyển tiếp dư (+) / thiếu (−) so với kỳ theo ngày chốt cũ.</param>
/// <param name="SuggestedAdjustDays">BL-BR-28: lệch ≤ 3 ngày ⇒ 0; còn lại đủ số ngày lệch. Chủ trọ chọn 0..dư (hoặc thiếu..0).</param>
/// <param name="Rooms">HĐ đang ở: tiền phòng 1 ngày (theo kỳ cũ) và số tiền ứng với số ngày gợi ý.</param>
public sealed record BillingChangePreviewDto(
    DateOnly? EffectiveFrom,
    DateOnly? TransitionEnd,
    int TransitionDays,
    int BaseDays,
    int DeviationDays,
    int SuggestedAdjustDays,
    bool HasDraftInvoices,
    IReadOnlyList<TransitionRoomDto> Rooms);

/// <summary>K4: xem trước kỳ chuyển tiếp khi đổi ngày chốt / thu trước–thu sau (chưa lưu).</summary>
public sealed record PreviewBillingChangeQuery(Guid PropertyId, int AnchorDay, ChargeMode ChargeMode) : IRequest<Result<BillingChangePreviewDto>>;

public sealed class PreviewBillingChangeQueryValidator : AbstractValidator<PreviewBillingChangeQuery>
{
    public PreviewBillingChangeQueryValidator()
    {
        RuleFor(x => x.AnchorDay).InclusiveBetween(BillingSchedule.MinAnchorDay, BillingSchedule.MaxAnchorDay).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.ChargeMode).IsInEnum();
    }
}

public sealed class PreviewBillingChangeHandler(IAppDbContext db) : IRequestHandler<PreviewBillingChangeQuery, Result<BillingChangePreviewDto>>
{
    public async ValueTask<Result<BillingChangePreviewDto>> Handle(PreviewBillingChangeQuery request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var hasDrafts = await db.Invoices.AnyAsync(i => i.PropertyId == property.Id && i.Status == InvoiceStatus.Draft, cancellationToken);
        if (await BillingChangePlanner.FirstUnbilledPeriodStartAsync(db, property, cancellationToken) is not { } from)
            return new BillingChangePreviewDto(null, null, 0, 0, 0, 0, hasDrafts, []);

        var transition = property.BillingSchedule.ChangeFrom(from, request.AnchorDay, request.ChargeMode, adjustDays: null)
            .StandardPeriodContaining(from);
        var contracts = await db.Contracts.AsNoTracking().Include(c => c.RentTerms)
            .Where(c => c.PropertyId == property.Id && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && c.BillingStartDate <= transition.End && (c.ActualEndDate == null || c.ActualEndDate >= from))
            .ToListAsync(cancellationToken);
        var roomIds = contracts.Select(c => c.RoomId).ToList();
        var roomCodes = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, cancellationToken);
        var rooms = contracts
            .Select(c => (Contract: c, Rent: c.CurrentRent(from) ?? c.CurrentRent(c.StartDate) ?? 0m))
            .Select(x => new TransitionRoomDto(x.Contract.Id, roomCodes[x.Contract.RoomId], x.Rent,
                Invoice.Money(x.Rent / transition.BaseDays), Invoice.Money(x.Rent * transition.AdjustDays / transition.BaseDays)))
            .OrderBy(r => r.RoomCode)
            .ToList();
        return new BillingChangePreviewDto(from, transition.End, transition.Days, transition.BaseDays, transition.DeviationDays,
            transition.AdjustDays, hasDrafts, rooms);
    }
}

/// <param name="TransitionAdjustDays">Số ngày tiền phòng cộng (+) / trừ (−) ở kỳ chuyển tiếp; null = gợi ý (BL-BR-28).</param>
public sealed record UpdatePropertyBillingCommand(Guid PropertyId, PropertyBillingInput Billing, int? TransitionAdjustDays)
    : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdatePropertyBillingCommandValidator : AbstractValidator<UpdatePropertyBillingCommand>
{
    public UpdatePropertyBillingCommandValidator()
    {
        RuleFor(x => x.Billing).NotNull().WithErrorCode("REQUIRED");
        this.BillingRules(x => x.Billing);
    }
}

/// <summary>
/// PR-BR-09 / K4: hạn thanh toán, tính kỳ lẻ, báo trước áp ngay. Đổi ngày chốt / thu trước–thu sau: khu chưa có phiếu ⇒ áp lại từ đầu; đã có
/// ⇒ từ kỳ chưa lập phiếu đầu tiên của khu với kỳ chuyển tiếp (đổi lại khi kỳ đó chưa lập phiếu ⇒ ghi đè). Còn phiếu nháp ⇒ 422.
/// </summary>
public sealed class UpdatePropertyBillingHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<UpdatePropertyBillingCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdatePropertyBillingCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Property>(request.PropertyId, cancellationToken);
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var billing = request.Billing;
        property.UpdateBillingTerms(billing.PaymentDueDays, billing.ProrationMode, billing.NoticeDays);
        var before = property.BillingScheduleEntries.ToList();
        var from = await BillingChangePlanner.FirstUnbilledPeriodStartAsync(db, property, cancellationToken);
        var changed = property.ChangeBillingCycle(billing.AnchorDay, billing.ChargeMode, from, request.TransitionAdjustDays);
        if (changed.IsFailure)
            return changed.Error!;
        if (!before.SequenceEqual(property.BillingScheduleEntries)
            && await db.Invoices.AnyAsync(i => i.PropertyId == property.Id && i.Status == InvoiceStatus.Draft, cancellationToken))
            return PropertyErrors.BillingDraftInvoicesExist;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Contracts;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Rooms;

namespace renting_room.Application.Properties;

// ============================================================ Cài đặt kỳ thu của khu (PR-BR-09, K4 — BL-BR-28)

internal static class BillingChangePlanner
{
    /// <summary>
    /// Đầu kỳ chuẩn đầu tiên chưa lập phiếu của khu = ngày sau kỳ chuẩn chứa phiếu (chưa hủy) muộn nhất. Null = khu chưa có phiếu ⇒ đổi cài đặt
    /// áp lại từ đầu, không cần kỳ chuyển tiếp.
    /// </summary>
    public static async Task<DateOnly?> FirstUnbilledPeriodStartAsync(IPropertyQueries properties, Property property, CancellationToken ct)
    {
        var lastEnd = await properties.GetLastInvoicePeriodEndAsync(property.Id, ct);
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

public sealed class PreviewBillingChangeHandler(IPropertyQueries properties, IContractQueries contracts, IRoomQueries rooms)
    : IRequestHandler<PreviewBillingChangeQuery, Result<BillingChangePreviewDto>>
{
    public async ValueTask<Result<BillingChangePreviewDto>> Handle(PreviewBillingChangeQuery request, CancellationToken cancellationToken)
    {
        var property = await properties.GetAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var hasDrafts = await properties.HasDraftInvoicesAsync(property.Id, cancellationToken);
        if (await BillingChangePlanner.FirstUnbilledPeriodStartAsync(properties, property, cancellationToken) is not { } from)
            return new BillingChangePreviewDto(null, null, 0, 0, 0, 0, hasDrafts, []);

        var transition = property.BillingSchedule.ChangeFrom(from, request.AnchorDay, request.ChargeMode, adjustDays: null)
            .StandardPeriodContaining(from);
        var active = await contracts.ListForBillingTransitionAsync(property.Id, from, transition.End, cancellationToken);
        var roomIds = active.Select(c => c.RoomId).ToList();
        var roomCodes = await rooms.GetCodesAsync(roomIds, cancellationToken);
        var transitionRooms = active
            .Select(c => (Contract: c, Rent: c.CurrentRent(from) ?? c.CurrentRent(c.StartDate) ?? 0m))
            .Select(x => new TransitionRoomDto(x.Contract.Id, roomCodes[x.Contract.RoomId], x.Rent,
                Invoice.Money(x.Rent / transition.BaseDays), Invoice.Money(x.Rent * transition.AdjustDays / transition.BaseDays)))
            .OrderBy(r => r.RoomCode)
            .ToList();
        return new BillingChangePreviewDto(from, transition.End, transition.Days, transition.BaseDays, transition.DeviationDays,
            transition.AdjustDays, hasDrafts, transitionRooms);
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
public sealed class UpdatePropertyBillingHandler(
    IPropertyRepository properties, IPropertyQueries queries, IOrganizationQueries organizations, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<UpdatePropertyBillingCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdatePropertyBillingCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var property = await properties.GetForUpdateAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var billing = request.Billing;
        property.UpdateBillingTerms(billing.PaymentDueDays, billing.ProrationMode, billing.NoticeDays, billing.RoundInvoiceTotal ?? property.RoundInvoiceTotal);
        var before = property.BillingScheduleEntries.ToList();
        var from = await BillingChangePlanner.FirstUnbilledPeriodStartAsync(queries, property, cancellationToken);
        var changed = property.ChangeBillingCycle(billing.AnchorDay, billing.ChargeMode, from, request.TransitionAdjustDays);
        if (changed.IsFailure)
            return changed.Error!;
        if (!before.SequenceEqual(property.BillingScheduleEntries)
            && await queries.HasDraftInvoicesAsync(property.Id, cancellationToken))
            return PropertyErrors.BillingDraftInvoicesExist;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await property.ToDetailAsync(organizations, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

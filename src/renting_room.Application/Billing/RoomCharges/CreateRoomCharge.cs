using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

// Tạo khoản phát sinh
/// <param name="Settlement">Có ⇒ khoản đã thanh toán / đã hoàn ngay (vẫn hiện trên phiếu, không tính vào tổng).</param>
public sealed record CreateRoomChargeCommand(
    Guid RoomId, RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn, string Reason, ChargeSettlement? Settlement)
    : IRequest<Result<RoomChargeDto>>;

public sealed class CreateRoomChargeCommandValidator : AbstractValidator<CreateRoomChargeCommand>
{
    public CreateRoomChargeCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Description).RequiredText(200, "Nội dung");
        RuleFor(x => x.Reason).RequiredText(300, "Lý do");
        RuleFor(x => x.Amount).Money(allowZero: false).LessThanOrEqualTo(RoomCharge.MaxAmount).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.IncurredOn).Must(d => d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode(RoomChargeErrors.InvalidIncurredDate.Code).WithMessage(RoomChargeErrors.InvalidIncurredDate.Message);
        RuleFor(x => x.Settlement!.Method).IsInEnum().When(x => x.Settlement is not null);
    }
}

/// <summary>
/// BL-UC-15 / BL-BR-30..33: tạo phụ thu / bù cho phòng đang có người thuê tại ngày phát sinh; nháp nhận được khoản đã có ⇒ gắn ngay,
/// chưa có ⇒ chờ. Khóa HĐ → phiếu (C-07).
/// </summary>
public sealed class CreateRoomChargeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CreateRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(CreateRoomChargeCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().Where(r => r.Id == request.RoomId).Select(r => new { r.Id, r.PropertyId }).FirstOrDefaultAsync(cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;

        // HĐ đang ở phòng tại ngày phát sinh — kể cả HĐ đã chuyển sang phòng khác sau đó (M05 CT-BR-14).
        var date = request.IncurredOn;
        var tenancy = await db.Contracts.AsNoTracking()
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                && ((c.RoomId == room.Id && c.RoomSince <= date && (c.ActualEndDate == null || c.ActualEndDate >= date))
                    || c.RoomMoves.Any(m => m.RoomId == room.Id && m.FromDate <= date && m.ToDate >= date)))
            .OrderByDescending(c => c.StartDate).Select(c => new { c.Id, c.Status }).FirstOrDefaultAsync(cancellationToken);
        if (tenancy is null)
            return RoomChargeErrors.NoTenant;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Contract>(tenancy.Id, cancellationToken);
        var status = await db.Contracts.Where(c => c.Id == tenancy.Id).Select(c => c.Status).FirstAsync(cancellationToken);
        var finalClosed = await db.Invoices.AnyAsync(
            i => i.ContractId == tenancy.Id && i.Type == InvoiceType.Final && i.Status == InvoiceStatus.Finalized, cancellationToken);
        if (status == ContractStatus.Ended || (status == ContractStatus.Liquidating && finalClosed))
            return RoomChargeErrors.NoOpenInvoice;

        // BL-BR-30 (G-01): khoản bù chưa trả không vượt một tháng tiền phòng ⇒ phiếu tháng luôn trừ được hết, không còn khoản bù treo.
        if (request.Kind == RoomChargeKind.Credit && request.Settlement is null
            && await CreditLimitExceededAsync(db, tenancy.Id, date, request.Amount, cancellationToken) is { } limit)
            return RoomChargeErrors.CreditTooHigh(limit);

        var charge = RoomCharge.Create(room.PropertyId, room.Id, tenancy.Id, request.Kind, request.Description, request.Amount, date, request.Reason);
        if (request.Settlement is { } settlement)
        {
            var settled = charge.Settle(settlement.SettledOn, settlement.Method, clock.GetUtcNow().ToBusinessDate());
            if (settled.IsFailure)
                return settled.Error!;
        }
        db.RoomCharges.Add(charge);
        if (await RoomChargeBilling.DraftForAsync(db, tenancy.Id, date, cancellationToken) is { } draft)
            RoomChargeBilling.Attach(draft, charge);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await RoomChargeBilling.ToDtosAsync(db, [charge], cancellationToken))[0];
    }

    /// <summary>Trả giới hạn (1 tháng tiền phòng) nếu khoản bù mới + các khoản bù chưa trả chưa vào phiếu đã chốt vượt giới hạn; null = hợp lệ.</summary>
    internal static async Task<decimal?> CreditLimitExceededAsync(IAppDbContext db, Guid contractId, DateOnly date, decimal amount, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().Include(c => c.RentTerms).FirstAsync(c => c.Id == contractId, ct);
        if (contract.CurrentRent(date) is not { } rent)
            return null;
        var waiting = await db.RoomCharges
            .Where(c => c.ContractId == contractId && c.Kind == RoomChargeKind.Credit && c.CancelledAt == null && !c.IsSettled
                && (c.InvoiceId == null || db.Invoices.Any(i => i.Id == c.InvoiceId && i.Status == InvoiceStatus.Draft)))
            .SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
        return waiting + amount > rent ? rent : null;
    }
}

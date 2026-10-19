using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Rooms;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

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
public sealed class CreateRoomChargeHandler(
    IUnitOfWork unitOfWork, IRoomQueries rooms, IContractQueries contracts, IInvoiceQueries invoiceQueries, IRoomChargeRepository roomChargeRepository,
    IRoomChargeQueries roomChargeQueries, RoomChargeBilling roomCharges, TimeProvider clock)
    : IRequestHandler<CreateRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(CreateRoomChargeCommand request, CancellationToken cancellationToken)
    {
        var propertyId = await rooms.GetPropertyIdAsync(request.RoomId, cancellationToken);
        if (propertyId is null)
            return PropertyErrors.RoomNotFound;

        // HĐ đang ở phòng tại ngày phát sinh — kể cả HĐ đã chuyển sang phòng khác sau đó (M05 CT-BR-14).
        var date = request.IncurredOn;
        var tenancy = await contracts.FindTenancyAsync(request.RoomId, date, cancellationToken);
        if (tenancy is null)
            return RoomChargeErrors.NoTenant;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(tenancy.Id, cancellationToken);
        var status = (await contracts.GetStatusAsync(tenancy.Id, cancellationToken))!.Value;
        var finalClosed = await invoiceQueries.HasFinalizedFinalAsync(tenancy.Id, cancellationToken);
        if (status == ContractStatus.Ended || (status == ContractStatus.Liquidating && finalClosed))
            return RoomChargeErrors.NoOpenInvoice;

        // BL-BR-30 (G-01): khoản bù chưa trả không vượt một tháng tiền phòng ⇒ phiếu tháng luôn trừ được hết, không còn khoản bù treo.
        if (request.Kind == RoomChargeKind.Credit && request.Settlement is null
            && await CreditLimitExceededAsync(contracts, roomChargeQueries, tenancy.Id, date, request.Amount, cancellationToken) is { } limit)
            return RoomChargeErrors.CreditTooHigh(limit);

        var charge = RoomCharge.Create(propertyId.Value, request.RoomId, tenancy.Id, request.Kind, request.Description, request.Amount, date, request.Reason);
        if (request.Settlement is { } settlement)
        {
            var settled = charge.Settle(settlement.SettledOn, settlement.Method, clock.GetUtcNow().ToBusinessDate());
            if (settled.IsFailure)
                return settled.Error!;
        }
        roomChargeRepository.Add(charge);
        if (await roomCharges.DraftForAsync(tenancy.Id, date, cancellationToken) is { } draft)
            RoomChargeBilling.Attach(draft, charge);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await roomCharges.ToDtosAsync([charge], cancellationToken))[0];
    }

    /// <summary>Trả giới hạn (1 tháng tiền phòng) nếu khoản bù mới + các khoản bù chưa trả chưa vào phiếu đã chốt vượt giới hạn; null = hợp lệ.</summary>
    internal static async Task<decimal?> CreditLimitExceededAsync(
        IContractQueries contracts, IRoomChargeQueries roomCharges, Guid contractId, DateOnly date, decimal amount, CancellationToken ct)
    {
        var contract = (await contracts.GetWithRentTermsAsync(contractId, ct))!;
        if (contract.CurrentRent(date) is not { } rent)
            return null;
        var waiting = await roomCharges.GetWaitingCreditTotalAsync(contractId, ct);
        return waiting + amount > rent ? rent : null;
    }
}

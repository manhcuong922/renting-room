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

public sealed class CreateRoomChargeHandler(
    IUnitOfWork unitOfWork, IRoomQueries rooms, IContractQueries contracts, IRoomChargeRepository roomChargeRepository,
    IRoomChargeQueries roomChargeQueries, RoomChargeBilling roomCharges, TimeProvider clock)
    : IRequestHandler<CreateRoomChargeCommand, Result<RoomChargeDto>>
{
    public async ValueTask<Result<RoomChargeDto>> Handle(CreateRoomChargeCommand request, CancellationToken cancellationToken)
    {
        var propertyId = await rooms.GetPropertyIdAsync(request.RoomId, cancellationToken);
        if (propertyId is null)
            return PropertyErrors.RoomNotFound;

        var date = request.IncurredOn;
        var tenancy = await contracts.FindTenancyAsync(request.RoomId, date, cancellationToken);
        if (tenancy is null)
            return RoomChargeErrors.NoTenant;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Contract>(tenancy.Id, cancellationToken);
        // Hóa đơn trả phòng đã chốt ⇒ khoản chờ, xác nhận khi hoàn tất thanh lý (M05 CT-BR-12); HĐ đã kết thúc ⇒ không còn nơi xử lý.
        if (await contracts.GetStatusAsync(tenancy.Id, cancellationToken) == ContractStatus.Ended)
            return RoomChargeErrors.NoOpenInvoice;

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

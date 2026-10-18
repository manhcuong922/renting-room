using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

// Phụ lục: đổi giá, gia hạn, báo trả phòng
/// <summary>
/// CT-UC-21: ký lại cho người còn ở — trong 1 transaction: HĐ cũ bắt đầu thanh lý tại ngày bàn giao X, tạo HĐ nháp mới từ X+1
/// (chép điều khoản, dịch vụ, người ở còn lại, xe của họ). Chủ trọ xem lại nháp rồi kích hoạt như bình thường.
/// </summary>
/// <param name="TransferDeposit">PM-BR-34: cọc của HĐ cũ ghi "Đã chuyển sang HĐ mới" (mặc định); false ⇒ HĐ cũ vẫn "Đang giữ cọc" để hoàn sau.</param>
public sealed record ResignContractCommand(
    Guid ContractId, DateOnly HandoverDate, Guid RepresentativeRenterId, DateOnly? EndDate, bool TransferDeposit = true)
    : IRequest<Result<CreatedWithWarnings>>;

public sealed class ResignContractCommandValidator : AbstractValidator<ResignContractCommand>
{
    public ResignContractCommandValidator()
    {
        RuleFor(x => x.RepresentativeRenterId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.EndDate)
            .Must((x, end) => end is null || end <= x.HandoverDate.AddYears(10))
            .WithErrorCode("INVALID_END_DATE").WithMessage("Thời hạn hợp đồng mới tối đa 10 năm.");
    }
}

public sealed class ResignContractHandler(
    IAppDbContext db, ICurrentUser currentUser, IContractNumberGenerator numberGenerator, TimeProvider clock)
    : IRequestHandler<ResignContractCommand, Result<CreatedWithWarnings>>
{
    public ValueTask<Result<CreatedWithWarnings>> Handle(ResignContractCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync<CreatedWithWarnings>(db, request.ContractId, async old =>
        {
            var data = old.ResignDraft(request.HandoverDate, request.RepresentativeRenterId, request.EndDate);
            if (data.IsFailure)
                return data.Error!;
            if (!await OccupantChecks.HasIdNumberAsync(db, request.RepresentativeRenterId, cancellationToken))
                return ContractErrors.RepresentativeIdRequired;

            if (await LiquidationGuards.HasInvoiceAfterAsync(db, old.Id, request.HandoverDate, cancellationToken))
                return ContractErrors.InvoiceAfterEndDate;
            var contractNo = await ContractNumbers.NextAsync(db, numberGenerator, currentUser, clock, cancellationToken);
            var liquidation = old.StartLiquidation(request.HandoverDate, TerminationReason.MutualAgreement, null,
                $"Ký lại cho người còn ở — hợp đồng mới {contractNo}", clock.GetUtcNow().ToBusinessDate());
            if (liquidation.IsFailure)
                return liquidation.Error!;

            var next = Contract.CreateDraft(old.PropertyId, old.RoomId, contractNo, data.Value!, previousContractId: old.Id);
            var renterIds = data.Value!.Occupants.Select(o => o.RenterId).ToList();
            var vehicles = old.ResignVehicles(renterIds, request.HandoverDate, next.StartDate);
            // Lưu việc kết thúc xe ở HĐ cũ trước khi đăng ký lại ở HĐ mới — unique "biển số đang đăng ký" kiểm từng câu lệnh.
            await db.SaveChangesAsync(cancellationToken);
            foreach (var vehicle in vehicles)
            {
                var registered = next.RegisterVehicle(vehicle);
                if (registered.IsFailure)
                    return registered.Error!;
            }

            db.Contracts.Add(next);
            if (request.TransferDeposit)
                old.MarkDepositTransferred(contractNo);
            return new CreatedWithWarnings(next.Id, ContractWarnings.For(next));
        }, cancellationToken));
}

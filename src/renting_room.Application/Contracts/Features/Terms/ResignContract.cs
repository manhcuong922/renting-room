using renting_room.Domain.Properties;
using renting_room.Application.Properties;
using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

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
    ContractMutation mutation, IUnitOfWork unitOfWork, IContractRepository contracts, IContractQueries contractQueries, IInvoiceQueries invoices,
    OccupantChecks occupantChecks, ICurrentUser currentUser, IContractNumberGenerator numberGenerator, LiquidationDrafts drafts,
    IPropertyQueries properties, TimeProvider clock)
    : IRequestHandler<ResignContractCommand, Result<CreatedWithWarnings>>
{
    public ValueTask<Result<CreatedWithWarnings>> Handle(ResignContractCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync<CreatedWithWarnings>(request.ContractId, async old =>
        {
            var data = old.ResignDraft(request.HandoverDate, request.RepresentativeRenterId, request.EndDate);
            if (data.IsFailure)
                return data.Error!;
            if (!await occupantChecks.HasIdNumberAsync(request.RepresentativeRenterId, cancellationToken))
                return ContractErrors.RepresentativeIdRequired;

            await drafts.DeleteAfterAsync(old.Id, request.HandoverDate, cancellationToken);
            if (await invoices.HasInvoiceAfterAsync(old.Id, request.HandoverDate, cancellationToken))
                return ContractErrors.InvoiceAfterEndDate;
            var contractNo = await ContractNumbers.NextAsync(contractQueries, numberGenerator, currentUser, clock, cancellationToken);
            // Người thuê không rời đi: HĐ mới tính tiếp từ X+1 theo "Tính kỳ lẻ" của khu ⇒ kỳ cuối HĐ cũ bù cho đúng 1 kỳ (CT-BR-11);
            // X là ngày cuối kỳ ⇒ HĐ mới bắt đầu kỳ sau, không chồng ngày ⇒ thu trọn kỳ.
            var billing = await properties.GetBillingAsync(old.PropertyId, cancellationToken);
            var finalCharge = billing.Schedule.StandardPeriodContaining(request.HandoverDate).End == request.HandoverDate ? FinalPeriodCharge.FullPeriod
                : billing.ProrationMode == ProrationMode.Daily ? FinalPeriodCharge.Daily
                : FinalPeriodCharge.None;
            var liquidation = old.StartLiquidation(request.HandoverDate, TerminationReason.MutualAgreement, null,
                $"Ký lại cho người còn ở — hợp đồng mới {contractNo}", clock.GetUtcNow().ToBusinessDate(), finalCharge);
            if (liquidation.IsFailure)
                return liquidation.Error!;

            var next = Contract.CreateDraft(old.PropertyId, old.RoomId, contractNo, data.Value!, previousContractId: old.Id);
            var renterIds = data.Value!.Occupants.Select(o => o.RenterId).ToList();
            var vehicles = old.ResignVehicles(renterIds, request.HandoverDate, next.StartDate, next.Id);
            // Lưu việc kết thúc xe ở HĐ cũ trước khi đăng ký lại ở HĐ mới — unique "biển số đang đăng ký" kiểm từng câu lệnh.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            foreach (var vehicle in vehicles)
            {
                var registered = next.RegisterVehicle(vehicle);
                if (registered.IsFailure)
                    return registered.Error!;
            }

            contracts.Add(next);
            if (request.TransferDeposit)
                old.MarkDepositTransferred(contractNo);
            return new CreatedWithWarnings(next.Id, ContractWarnings.For(next));
        }, cancellationToken));
}

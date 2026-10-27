using FluentValidation;
using Mediator;
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

/// <summary>
/// M05 CT-UC-21 (27/10/2026): chỉ soạn HĐ nháp nối tiếp — KHÔNG đổi HĐ cũ (vẫn hiệu lực, hóa đơn / xe / cọc nguyên vẹn), hủy nháp không có gì
/// phải hoàn nguyên. Thanh lý HĐ cũ, chuyển xe và cọc chỉ xảy ra khi ký (<see cref="ResignLiquidation"/>, gọi từ lệnh kích hoạt).
/// </summary>
public sealed class ResignContractHandler(
    ContractMutation mutation, IContractRepository contracts, IContractQueries contractQueries, OccupantChecks occupantChecks,
    ICurrentUser currentUser, IContractNumberGenerator numberGenerator, TimeProvider clock)
    : IRequestHandler<ResignContractCommand, Result<CreatedWithWarnings>>
{
    public ValueTask<Result<CreatedWithWarnings>> Handle(ResignContractCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync<CreatedWithWarnings>(request.ContractId, async old =>
        {
            var data = old.ResignDraft(request.HandoverDate, request.RepresentativeRenterId, request.EndDate);
            if (data.IsFailure)
                return data.Error!;
            if (await contracts.FindResignDraftAsync(old.Id, cancellationToken) is not null)
                return ContractErrors.ResignDraftExists;
            if (!await occupantChecks.HasIdNumberAsync(request.RepresentativeRenterId, cancellationToken))
                return ContractErrors.RepresentativeIdRequired;

            var contractNo = await ContractNumbers.NextAsync(contractQueries, numberGenerator, currentUser, clock, cancellationToken);
            var next = Contract.CreateDraft(old.PropertyId, old.RoomId, contractNo, data.Value!, old.Id, request.TransferDeposit);
            contracts.Add(next);
            return new CreatedWithWarnings(next.Id, ContractWarnings.For(next));
        }, cancellationToken));
}

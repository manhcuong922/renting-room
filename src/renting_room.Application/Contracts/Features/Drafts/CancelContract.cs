using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

public sealed record CancelContractCommand(Guid Id, string Reason) : IRequest<Result<IReadOnlyList<Warning>>>;

public sealed class CancelContractCommandValidator : AbstractValidator<CancelContractCommand>
{
    public CancelContractCommandValidator() => RuleFor(x => x.Reason).RequiredText(500, "Lý do hủy");
}

/// <summary>
/// Hủy HĐ nháp. Nháp ký lại (CT-UC-21) không đổi HĐ cũ nên không có gì phải hoàn nguyên (xe, cọc, ngày trả phòng của HĐ cũ giữ nguyên).
/// </summary>
public sealed class CancelContractHandler(ContractMutation mutation, TimeProvider clock)
    : IRequestHandler<CancelContractCommand, Result<IReadOnlyList<Warning>>>
{
    public ValueTask<Result<IReadOnlyList<Warning>>> Handle(CancelContractCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync<IReadOnlyList<Warning>>(request.Id, contract =>
        {
            var cancelled = contract.Cancel(request.Reason, clock.GetUtcNow());
            return Task.FromResult(cancelled.IsFailure ? cancelled.Error! : Result.Success<IReadOnlyList<Warning>>([]));
        }, cancellationToken));
}

using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

// Phụ lục: đổi giá, gia hạn, báo trả phòng
/// <summary>CT-UC-22: HĐ quá hạn, chủ trọ cho ở tiếp chưa ký lại (CT-BR-45).</summary>
public sealed record StartHoldoverCommand(Guid ContractId, string? Note) : IRequest<Result>;

public sealed class StartHoldoverCommandValidator : AbstractValidator<StartHoldoverCommand>
{
    public StartHoldoverCommandValidator() => RuleFor(x => x.Note).OptionalText(500);
}

public sealed class StartHoldoverHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<StartHoldoverCommand, Result>
{
    public ValueTask<Result> Handle(StartHoldoverCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId,
            contract => Task.FromResult(contract.StartHoldover(clock.GetUtcNow().ToBusinessDate(), request.Note)), cancellationToken));
}

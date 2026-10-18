using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Properties;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

// Phụ lục: đổi giá, gia hạn, báo trả phòng
/// <param name="EffectiveFrom">Bỏ trống ⇒ kỳ chưa chốt đầu tiên (CT-UC-05: sửa trực tiếp trên HĐ, lịch sử giá giữ nguyên).</param>
public sealed record ChangeRentCommand(Guid ContractId, DateOnly? EffectiveFrom, decimal MonthlyRent, string? AddendumNo, string? Note)
    : IRequest<Result>;

public sealed class ChangeRentCommandValidator : AbstractValidator<ChangeRentCommand>
{
    public ChangeRentCommandValidator()
    {
        RuleFor(x => x.MonthlyRent).Money(allowZero: false);
        RuleFor(x => x.AddendumNo).OptionalText(30);
        RuleFor(x => x.Note).OptionalText(500);
    }
}

public sealed class ChangeRentHandler(IAppDbContext db, IInvoiceLockReader invoiceLocks) : IRequestHandler<ChangeRentCommand, Result>
{
    public ValueTask<Result> Handle(ChangeRentCommand request, CancellationToken cancellationToken) =>
        new(ContractMutation.RunAsync(db, request.ContractId, async contract =>
        {
            var firstOpenPeriod = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, cancellationToken);
            var schedule = (await PropertyBilling.LoadAsync(db, contract.PropertyId, cancellationToken)).Schedule;
            return contract.ChangeRent(request.EffectiveFrom ?? firstOpenPeriod ?? contract.StartDate, request.MonthlyRent, request.AddendumNo,
                request.Note, firstOpenPeriod, schedule);
        }, cancellationToken));
}

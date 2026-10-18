using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Ngân hàng & nội quy
/// <summary>Cả 3 trường null = xóa tài khoản ngân hàng.</summary>
public sealed record UpdateBankAccountCommand(Guid PropertyId, string? BankName, string? AccountNo, string? AccountName)
    : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdateBankAccountCommandValidator : AbstractValidator<UpdateBankAccountCommand>
{
    public UpdateBankAccountCommandValidator()
    {
        When(x => x.BankName is not null || x.AccountNo is not null || x.AccountName is not null, () =>
        {
            RuleFor(x => x.BankName!).RequiredText(100, "Tên ngân hàng");
            RuleFor(x => x.AccountName!).RequiredText(200, "Tên chủ tài khoản");
            RuleFor(x => x.AccountNo)
                .NotEmpty().WithErrorCode("REQUIRED")
                .Matches(@"^\d{6,20}$").WithErrorCode("INVALID_BANK_ACCOUNT").WithMessage("Số tài khoản gồm 6–20 chữ số.");
        });
    }
}

public sealed class UpdateBankAccountHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateBankAccountCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.UpdateBankAccount(request.AccountNo is null
            ? null
            : new BankAccount(request.BankName!, request.AccountNo, request.AccountName!));
        await db.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

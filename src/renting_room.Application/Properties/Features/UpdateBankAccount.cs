using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Identity;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Properties;

/// <summary>
/// Cả 3 trường null = xóa tài khoản ngân hàng. Chỉ chủ trọ (chặn ở API); đổi / xóa phải kèm <paramref name="CurrentPassword"/> = mật khẩu đăng nhập
/// (H-06, M02 §9) — gửi lại đúng tài khoản đang có thì không cần.
/// </summary>
public sealed record UpdateBankAccountCommand(Guid PropertyId, string? BankName, string? AccountNo, string? AccountName, string? CurrentPassword = null)
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

public sealed class UpdateBankAccountHandler(
    IPropertyRepository properties, IOrganizationQueries organizations, IUnitOfWork unitOfWork, PasswordConfirmation passwordConfirmation,
    TimeProvider clock)
    : IRequestHandler<UpdateBankAccountCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var property = await properties.GetAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var account = request.AccountNo is null
            ? null
            : new BankAccount(request.BankName!.Trim(), request.AccountNo.Trim(), request.AccountName!.Trim());
        if (account != property.BankAccount)
        {
            var confirmed = await passwordConfirmation.ConfirmCurrentUserAsync(request.CurrentPassword, cancellationToken);
            if (confirmed.IsFailure)
                return confirmed.Error!;
        }

        property.UpdateBankAccount(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(organizations, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

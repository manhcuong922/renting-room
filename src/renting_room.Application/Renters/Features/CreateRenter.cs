using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public sealed record CreateRenterCommand(RenterInput Renter) : IRequest<Result<Guid>>;

public sealed class CreateRenterCommandValidator : AbstractValidator<CreateRenterCommand>
{
    // POST /renters gửi hồ sơ ở cấp gốc body (khác PUT bọc trong "renter").
    public CreateRenterCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Renter).NotNull();
        RuleFor(x => x.Renter).FlattenedValidator(new RenterInputValidator(clock, idNumberRequired: true));
    }
}

/// <summary>RT-BR-02: cùng số giấy tờ trong tổ chức ⇒ 409, dùng lại hồ sơ cũ (unique index là chốt chặn khi gửi song song).</summary>
public sealed class CreateRenterHandler(
    IRenterRepository renters, IUnitOfWork unitOfWork, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<CreateRenterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRenterCommand request, CancellationToken cancellationToken)
    {
        var idNumber = request.Renter.IdNumber is { } number
            ? protector.ProtectIdNumber(currentUser.OrganizationId!.Value, request.Renter.IdType!.Value, number)
            : null;
        if (idNumber is not null
            && await renters.FindIdByIdNumberHashAsync(idNumber.Hash, excludeId: null, cancellationToken) is { } existing)
        {
            // ID-BR-26: người thuê cũ của khu khác quay lại ⇒ quản lý khu nhập đúng số giấy tờ được dùng lại hồ sơ.
            var (fullName, last4) = await renters.ClaimAsync(existing, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            // M-13: 409 này trả họ tên + 4 số cuối của hồ sơ có sẵn ⇒ là đường dò CCCD, ghi dấu vết (không ghi số đã nhập).
            await auditTrail.RecordReadAsync(AuditActions.IdNumberLookup, nameof(Renter), existing, new { source = "Create" }, cancellationToken);
            return RenterErrors.IdNumberExistsFor(existing, fullName, last4);
        }

        var renter = Renter.Create(request.Renter.ToProfile(), idNumber);
        renters.Add(renter);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return renter.Id;
    }
}

using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
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
public sealed class CreateRenterHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector)
    : IRequestHandler<CreateRenterCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRenterCommand request, CancellationToken cancellationToken)
    {
        var idNumber = request.Renter.IdNumber is { } number
            ? protector.ProtectIdNumber(currentUser.OrganizationId!.Value, request.Renter.IdType!.Value, number)
            : null;
        if (idNumber is not null
            && await db.Renters.Where(r => r.IdNumberHash == idNumber.Hash).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
            return RenterErrors.IdNumberExistsFor(existing);

        var renter = Renter.Create(request.Renter.ToProfile(), idNumber);
        db.Renters.Add(renter);
        await db.SaveChangesAsync(cancellationToken);
        return renter.Id;
    }
}

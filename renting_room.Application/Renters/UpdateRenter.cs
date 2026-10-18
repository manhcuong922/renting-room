using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public sealed record UpdateRenterCommand(Guid Id, RenterInput Renter, uint Version) : IRequest<Result<RenterDto>>;

public sealed class UpdateRenterCommandValidator : AbstractValidator<UpdateRenterCommand>
{
    public UpdateRenterCommandValidator(TimeProvider clock) =>
        RuleFor(x => x.Renter).NotNull().SetValidator(new RenterInputValidator(clock, idNumberRequired: false));
}

public sealed class UpdateRenterHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<UpdateRenterCommand, Result<RenterDto>>
{
    public async ValueTask<Result<RenterDto>> Handle(UpdateRenterCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (renter is null)
            return RenterErrors.RenterNotFound;
        if (renter.IsAnonymized)
            return RenterErrors.Anonymized;

        // idType null + idNumber null = không có giấy tờ (chỉ khi dưới 14 tuổi); idNumber null + cùng loại = giữ số cũ.
        ProtectedIdNumber? idNumber;
        if (request.Renter.IdNumber is { } number)
            idNumber = protector.ProtectIdNumber(currentUser.OrganizationId!.Value, request.Renter.IdType!.Value, number);
        else if (request.Renter.IdType is null)
        {
            if (renter.HasIdNumber && request.Renter.DateOfBirth.AgeOn(clock.GetUtcNow().ToBusinessDate()) >= Renter.IdRequiredAge)
                return RenterErrors.IdNumberRequired;
            idNumber = null;
        }
        else if (request.Renter.IdType == renter.IdType && renter.HasIdNumber)
            idNumber = new ProtectedIdNumber(renter.IdType.Value, renter.IdNumberEncrypted!, renter.IdNumberHash!, renter.IdNumberLast4!);
        else
            return Error.Validation("ID_NUMBER_REQUIRED", "Đổi loại giấy tờ thì phải nhập lại số giấy tờ.");

        if (idNumber is not null && idNumber.Hash != renter.IdNumberHash
            && await db.Renters.Where(r => r.IdNumberHash == idNumber.Hash && r.Id != renter.Id)
                .Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
            return RenterErrors.IdNumberExistsFor(existing);

        db.SetExpectedVersion(renter, request.Version);
        renter.Update(request.Renter.ToProfile(), idNumber);
        await db.SaveChangesAsync(cancellationToken);

        return await db.Renters.AsNoTracking().Where(r => r.Id == renter.Id).ToDto().FirstAsync(cancellationToken);
    }
}

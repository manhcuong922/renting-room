using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// RT-UC-05: ghi nhận đồng ý xử lý dữ liệu cá nhân
public sealed record WithdrawRenterConsentCommand(Guid RenterId, Guid ConsentId, DateOnly WithdrawnOn) : IRequest<Result>;

public sealed class WithdrawRenterConsentHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<WithdrawRenterConsentCommand, Result>
{
    public async ValueTask<Result> Handle(WithdrawRenterConsentCommand request, CancellationToken cancellationToken)
    {
        var consent = await db.RenterConsents.FirstOrDefaultAsync(c => c.Id == request.ConsentId && c.RenterId == request.RenterId, cancellationToken);
        if (consent is null)
            return Result.Failure(RenterConsentErrors.NotFound);
        if (request.WithdrawnOn > clock.GetUtcNow().ToBusinessDate())
            return Result.Failure(RenterConsentErrors.InvalidWithdrawDate);

        var withdrawn = consent.Withdraw(request.WithdrawnOn);
        if (withdrawn.IsFailure)
            return withdrawn;
        await db.SaveChangesAsync(cancellationToken);
        return withdrawn;
    }
}

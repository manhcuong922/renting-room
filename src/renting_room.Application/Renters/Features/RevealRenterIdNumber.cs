using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Renters;
using renting_room.Application.Identity;

namespace renting_room.Application.Renters;

/// <summary>Xem số giấy tờ đầy đủ — chỉ người có quyền (ID-BR-22), luôn ghi log ai xem (C-10).</summary>
public sealed record RevealRenterIdNumberQuery(Guid Id) : IRequest<Result<string>>;

public sealed class RevealRenterIdNumberHandler(
    IRenterQueries renters, IUserQueries users, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealRenterIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealRenterIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(users, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var renter = await renters.GetIdNumberAsync(request.Id, cancellationToken);
        if (renter is null)
            return RenterErrors.RenterNotFound;
        if (renter.IsAnonymized)
            return RenterErrors.Anonymized;
        if (renter.Encrypted is not { Length: > 0 } encrypted)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Renter), request.Id);
        return protector.Decrypt(encrypted);
    }
}

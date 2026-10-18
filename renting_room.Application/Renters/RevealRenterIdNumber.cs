using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// Queries
/// <summary>Xem số giấy tờ đầy đủ — chỉ người có quyền (ID-BR-22), luôn ghi log ai xem (C-10).</summary>
public sealed record RevealRenterIdNumberQuery(Guid Id) : IRequest<Result<string>>;

public sealed class RevealRenterIdNumberHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealRenterIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealRenterIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var renter = await db.Renters.AsNoTracking()
            .Where(r => r.Id == request.Id).Select(r => new { r.IdNumberEncrypted, r.AnonymizedAt }).FirstOrDefaultAsync(cancellationToken);
        if (renter is null)
            return RenterErrors.RenterNotFound;
        if (renter.AnonymizedAt is not null)
            return RenterErrors.Anonymized;
        if (renter.IdNumberEncrypted is not { Length: > 0 } encrypted)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Renter), request.Id);
        return protector.Decrypt(encrypted);
    }
}

using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Properties;

/// <summary>Xem số giấy tờ đầy đủ của chủ trọ (bên cho thuê mặc định) — như <see cref="RevealLessorIdNumberQuery"/>.</summary>
public sealed record RevealOrganizationLessorIdNumberQuery : IRequest<Result<string>>;

public sealed class RevealOrganizationLessorIdNumberHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealOrganizationLessorIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealOrganizationLessorIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(db, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var organizationId = currentUser.OrganizationId!.Value;
        var lessor = await LessorSource.OrganizationLessorAsync(db, organizationId, cancellationToken);
        if (lessor?.IdNumberEncrypted is null)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Organization), organizationId);
        return protector.Decrypt(lessor.IdNumberEncrypted);
    }
}

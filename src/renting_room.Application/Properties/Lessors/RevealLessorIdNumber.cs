using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;
using renting_room.Application.Identity;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>
/// Xem số giấy tờ đầy đủ của bên cho thuê hiệu lực của khu (riêng của khu, không có thì của chủ trọ) — chỉ người có quyền (ID-BR-22),
/// luôn ghi log ai xem (C-10, ID-BR-21).
/// </summary>
public sealed record RevealLessorIdNumberQuery(Guid PropertyId) : IRequest<Result<string>>;

public sealed class RevealLessorIdNumberHandler(
    IPropertyQueries properties, IOrganizationQueries organizations, IUserQueries users, ICurrentUser currentUser,
    IPersonalDataProtector protector, IAuditTrail auditTrail)
    : IRequestHandler<RevealLessorIdNumberQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(RevealLessorIdNumberQuery request, CancellationToken cancellationToken)
    {
        if (!await SensitiveDataAccess.CanViewAsync(users, currentUser, cancellationToken))
            return IdentityErrors.SensitiveDataForbidden;

        var property = await properties.GetAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        var (lessor, _) = await LessorSource.EffectiveAsync(organizations, property, cancellationToken);
        if (lessor?.IdNumberEncrypted is null)
            return string.Empty;

        auditTrail.RecordRead(AuditActions.RevealIdNumber, nameof(Property), request.PropertyId);
        return protector.Decrypt(lessor.IdNumberEncrypted);
    }
}

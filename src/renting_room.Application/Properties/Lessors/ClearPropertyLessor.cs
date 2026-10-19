using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>Bỏ bên cho thuê riêng của khu ⇒ dùng lại thông tin chủ trọ (PR-BR-17). HĐ đã kích hoạt giữ bản chụp.</summary>
public sealed record ClearPropertyLessorCommand(Guid PropertyId) : IRequest<Result<PropertyDetailDto>>;

public sealed class ClearPropertyLessorHandler(
    IPropertyRepository properties, IOrganizationQueries organizations, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ClearPropertyLessorCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(ClearPropertyLessorCommand request, CancellationToken cancellationToken)
    {
        var property = await properties.GetAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.ClearLessor();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(organizations, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

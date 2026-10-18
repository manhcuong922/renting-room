using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Bên cho thuê (PR-BR-12 → 17)
/// <summary>Bỏ bên cho thuê riêng của khu ⇒ dùng lại thông tin chủ trọ (PR-BR-17). HĐ đã kích hoạt giữ bản chụp.</summary>
public sealed record ClearPropertyLessorCommand(Guid PropertyId) : IRequest<Result<PropertyDetailDto>>;

public sealed class ClearPropertyLessorHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ClearPropertyLessorCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(ClearPropertyLessorCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.ClearLessor();
        await db.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

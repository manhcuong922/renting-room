using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Ngân hàng & nội quy
public sealed record UpdateHouseRulesCommand(Guid PropertyId, string? Text) : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdateHouseRulesCommandValidator : AbstractValidator<UpdateHouseRulesCommand>
{
    public UpdateHouseRulesCommandValidator() => RuleFor(x => x.Text).OptionalText(20_000);
}

public sealed class UpdateHouseRulesHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateHouseRulesCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateHouseRulesCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        property.UpdateHouseRules(request.Text);
        await db.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

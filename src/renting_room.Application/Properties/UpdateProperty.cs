using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

// Tạo / sửa khu
public sealed record UpdatePropertyCommand(
    Guid Id,
    string Name,
    AddressInput Address,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput? Land,
    uint Version) : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdatePropertyCommandValidator : AbstractValidator<UpdatePropertyCommand>
{
    public UpdatePropertyCommandValidator()
    {
        RuleFor(x => x.Name).RequiredText(200, "Tên khu trọ");
        RuleFor(x => x.Description).OptionalText(2000);
        RuleFor(x => x.EvnCustomerCode).OptionalText(20);
        this.AddressRules(x => x.Address);
        this.LandRules(x => x.Land);
    }
}

/// <summary>Sửa thông tin khu. Cài đặt kỳ thu đổi qua <see cref="UpdatePropertyBillingCommand"/> (K4).</summary>
public sealed class UpdatePropertyHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdatePropertyCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdatePropertyCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        db.SetExpectedVersion(property, request.Version);
        property.UpdateInfo(request.Name, request.Address.ToDomain(), request.Description, request.EvnCustomerCode, request.Land.ToDomain());
        await db.SaveChangesAsync(cancellationToken);

        return await property.ToDetailAsync(db, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}

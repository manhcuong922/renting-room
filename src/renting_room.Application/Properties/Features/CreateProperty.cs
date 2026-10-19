using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;
using renting_room.Application.Fees;

namespace renting_room.Application.Properties;

public sealed record CreatePropertyCommand(
    string Code,
    string Name,
    AddressInput Address,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput? Land,
    PropertyBillingInput? Billing) : IRequest<Result<Guid>>;

public sealed class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
    public CreatePropertyCommandValidator()
    {
        RuleFor(x => x.Code).Code(32, "Mã khu trọ");
        RuleFor(x => x.Name).RequiredText(200, "Tên khu trọ");
        RuleFor(x => x.Description).OptionalText(2000);
        RuleFor(x => x.EvnCustomerCode).OptionalText(20);
        this.AddressRules(x => x.Address);
        this.BillingRules(x => x.Billing);
        this.LandRules(x => x.Land);
    }
}

public sealed class CreatePropertyHandler(IPropertyRepository properties, IFeeTypeRepository fees, IUnitOfWork unitOfWork)
    : IRequestHandler<CreatePropertyCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreatePropertyCommand request, CancellationToken cancellationToken)
    {
        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await properties.IsCodeTakenAsync(code, cancellationToken))
            return PropertyErrors.PropertyCodeTaken;

        var property = Property.Create(
            code, request.Name, request.Address.ToDomain(), request.Billing?.ToDomain() ?? BillingSettings.Standard);
        property.UpdateInfo(request.Name, request.Address.ToDomain(), request.Description, request.EvnCustomerCode, request.Land.ToDomain());

        properties.Add(property);
        // FE-UC-01: khu mới có sẵn Điện + Nước theo công tơ (chưa có giá), tự gắn vào HĐ mới.
        foreach (var fee in FeeType.DefaultsFor(property.Id))
            fees.Add(fee);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return property.Id;
    }
}

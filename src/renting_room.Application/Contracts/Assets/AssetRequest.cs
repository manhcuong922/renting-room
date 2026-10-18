using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public sealed record AssetRequest(string Name, int Quantity, string? ConditionAtHandover, decimal? ValueEstimate, string? Note)
{
    public AssetInput ToDomain() => new(Name, Quantity, ConditionAtHandover, ValueEstimate, Note);
}

internal sealed class AssetRequestValidator : AbstractValidator<AssetRequest>
{
    public AssetRequestValidator()
    {
        RuleFor(x => x.Name).RequiredText(200, "Tên tài sản");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 100).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.ConditionAtHandover).OptionalText(500);
        RuleFor(x => x.ValueEstimate).OptionalMoney();
        RuleFor(x => x.Note).OptionalText(500);
    }
}

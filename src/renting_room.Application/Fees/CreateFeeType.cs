using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Fees;

// Tạo / sửa
public sealed record CreateFeeTypeCommand(
    Guid PropertyId,
    string Name,
    FeeGroup Group,
    ChargeBasis? ChargeBasis,
    string Unit,
    bool AutoAttach,
    decimal? DefaultQuantity,
    int? SortOrder,
    FeePriceInput? InitialPrice,
    VehicleType? VehicleType = null) : IRequest<Result<CreatedWithWarnings>>;

public sealed class CreateFeeTypeCommandValidator : AbstractValidator<CreateFeeTypeCommand>
{
    public CreateFeeTypeCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên khoản thu");
        RuleFor(x => x.Unit).RequiredText(20, "Đơn vị");
        RuleFor(x => x.Group).IsInEnum();
        RuleFor(x => x.ChargeBasis).NotNull().When(x => x.Group == FeeGroup.Service)
            .WithErrorCode("REQUIRED").WithMessage("Chọn cách tính dịch vụ: theo phòng, theo đầu người hay theo số gói.");
        RuleFor(x => x.ChargeBasis).Null().When(x => x.Group != FeeGroup.Service)
            .WithErrorCode("INVALID_CHARGE_BASIS").WithMessage("Điện nước theo công tơ không chọn cách tính dịch vụ.");
        RuleFor(x => x.ChargeBasis).IsInEnum().When(x => x.ChargeBasis is not null);
        RuleFor(x => x.DefaultQuantity)
            .Must(q => q is null || (q > 0 && q <= FeeType.MaxQuantity && decimal.Round(q.Value, 2) == q))
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Số lượng mặc định 0–100, tối đa 2 số lẻ.");
        RuleFor(x => x.DefaultQuantity).Null().When(x => x.ChargeBasis != ChargeBasis.PerUnit)
            .WithErrorCode("INVALID_DEFAULT_QUANTITY").WithMessage("Chỉ dịch vụ theo số gói mới có số gói mặc định.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).When(x => x.SortOrder is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.VehicleType).IsInEnum().When(x => x.VehicleType is not null);
        RuleFor(x => x.VehicleType).Null().When(x => x.ChargeBasis != ChargeBasis.PerUnit)
            .WithErrorCode("INVALID_VEHICLE_TYPE").WithMessage("Chỉ dịch vụ theo số gói (phí giữ xe) mới gắn loại xe.");
        this.PriceRules(x => x.InitialPrice, x => x.Group, "initialPrice.", clock);
    }
}

public sealed class CreateFeeTypeHandler(IAppDbContext db, IFeeSettings settings) : IRequestHandler<CreateFeeTypeCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(CreateFeeTypeCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        if (property.IsArchived)
            return PropertyErrors.PropertyArchived;

        var normalized = FeeType.NormalizeName(request.Name);
        if (await db.FeeTypes.AnyAsync(f => f.PropertyId == request.PropertyId && f.NameNormalized == normalized && f.ArchivedAt == null, cancellationToken))
            return FeeErrors.NameTaken;

        var fee = FeeType.Create(request.PropertyId, request.Name, request.Group, request.ChargeBasis, request.Unit, request.AutoAttach,
            request.DefaultQuantity, request.SortOrder ?? 100, vehicleType: request.VehicleType);
        IReadOnlyList<Warning> warnings = [];
        if (request.InitialPrice is { } price)
        {
            var added = fee.AddPrice(price.EffectiveFrom, price.UnitPrice ?? 0, price.Note, lockedUntil: null, price.Tiers);
            if (added.IsFailure)
                return added.Error!;
            warnings = FeeWarnings.ForPrice(fee, added.Value!, settings);
        }

        db.FeeTypes.Add(fee);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedWithWarnings(fee.Id, warnings);
    }
}

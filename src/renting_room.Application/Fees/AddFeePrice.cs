using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

// Bảng giá
public sealed record AddFeePriceCommand(Guid FeeTypeId, FeePriceInput Price) : IRequest<Result<CreatedWithWarnings>>;

public sealed class AddFeePriceCommandValidator : AbstractValidator<AddFeePriceCommand>
{
    // Giới hạn giá theo nhóm kiểm lại ở handler (validator chưa biết nhóm) ⇒ ở đây dùng mức rộng của nhóm Fixed.
    public AddFeePriceCommandValidator(TimeProvider clock) => this.PriceRules(x => x.Price, _ => null, "", clock);
}

public sealed class AddFeePriceHandler(IAppDbContext db, IFeePriceLockReader locks, IFeeSettings settings)
    : IRequestHandler<AddFeePriceCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(AddFeePriceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<FeeType>(request.FeeTypeId, cancellationToken);

        var fee = await db.FeeTypes.Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null)
            return FeeErrors.NotFound;
        var price = request.Price;
        if (price.UnitPrice is { } unit && !FeeRules.IsValidPrice(unit, fee.Group))
            return Error.Validation("INVALID_AMOUNT", $"Đơn giá tối đa {FeeRules.MaxPrice(fee.Group):N0}đ, tối đa 2 số lẻ.");

        var lockedUntil = await locks.GetLockedUntilAsync(fee.Id, cancellationToken);
        var added = fee.AddPrice(price.EffectiveFrom, price.UnitPrice ?? 0, price.Note, lockedUntil, price.Tiers);
        if (added.IsFailure)
            return added.Error!;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreatedWithWarnings(added.Value!.Id, FeeWarnings.ForPrice(fee, added.Value!, settings));
    }
}

using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Application.Fees;

// Tạo / sửa
public sealed record UpdateFeeTypeCommand(
    Guid Id, string Name, string Unit, bool AutoAttach, decimal? DefaultQuantity, int SortOrder, uint Version, VehicleType? VehicleType = null)
    : IRequest<Result<FeeTypeDto>>;

public sealed class UpdateFeeTypeCommandValidator : AbstractValidator<UpdateFeeTypeCommand>
{
    public UpdateFeeTypeCommandValidator()
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên khoản thu");
        RuleFor(x => x.Unit).RequiredText(20, "Đơn vị");
        RuleFor(x => x.DefaultQuantity)
            .Must(q => q is null || (q > 0 && q <= FeeType.MaxQuantity && decimal.Round(q.Value, 2) == q))
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Số lượng mặc định 0–100, tối đa 2 số lẻ.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).WithErrorCode("OUT_OF_RANGE");
    }
}

/// <summary>FE-BR-02: không đổi nhóm / cách tính — muốn đổi thì tạo khoản mới và ngừng dùng khoản cũ.</summary>
public sealed class UpdateFeeTypeHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateFeeTypeCommand, Result<FeeTypeDto>>
{
    public async ValueTask<Result<FeeTypeDto>> Handle(UpdateFeeTypeCommand request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.Include(f => f.Prices).FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (fee is null)
            return FeeErrors.NotFound;
        if (!fee.IsPerUnit && (request.DefaultQuantity is not null || request.VehicleType is not null))
            return Error.Validation("INVALID_DEFAULT_QUANTITY", "Chỉ dịch vụ theo số gói mới có số gói mặc định / loại xe.");

        var normalized = FeeType.NormalizeName(request.Name);
        if (!fee.IsArchived && await db.FeeTypes.AnyAsync(f => f.PropertyId == fee.PropertyId && f.NameNormalized == normalized
                && f.ArchivedAt == null && f.Id != fee.Id, cancellationToken))
            return FeeErrors.NameTaken;

        db.SetExpectedVersion(fee, request.Version);
        fee.Update(request.Name, request.Unit, request.AutoAttach, request.DefaultQuantity, request.SortOrder, request.VehicleType);
        await db.SaveChangesAsync(cancellationToken);
        return FeeTypeDto.From(fee, clock.GetUtcNow().ToBusinessDate());
    }
}

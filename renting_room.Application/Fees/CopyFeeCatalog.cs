using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Fees;

public sealed record CopyFeeCatalogCommand(Guid SourcePropertyId, Guid TargetPropertyId, bool IncludePrices, DateOnly? EffectiveFrom)
    : IRequest<Result<CopyFeeCatalogResult>>;

public sealed record CopyFeeCatalogResult(int Copied, IReadOnlyList<string> Skipped);

/// <summary>
/// Chép khoản thu chưa ngừng dùng từ khu nguồn sang khu đích; trùng tên / trùng mã hệ thống (điện, nước) thì bỏ qua.
/// Kèm giá: chép giá đang hiệu lực, hiệu lực từ <c>effectiveFrom</c> (mặc định hôm nay).
/// </summary>
public sealed class CopyFeeCatalogHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CopyFeeCatalogCommand, Result<CopyFeeCatalogResult>>
{
    public async ValueTask<Result<CopyFeeCatalogResult>> Handle(CopyFeeCatalogCommand request, CancellationToken cancellationToken)
    {
        var ids = new[] { request.SourcePropertyId, request.TargetPropertyId };
        var properties = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (properties.Count != ids.Distinct().Count() || request.SourcePropertyId == request.TargetPropertyId)
            return PropertyErrors.PropertyNotFound;
        if (properties.Single(p => p.Id == request.TargetPropertyId).IsArchived)
            return PropertyErrors.PropertyArchived;

        var source = await db.FeeTypes.AsNoTracking().Include(f => f.Prices)
            .Where(f => f.PropertyId == request.SourcePropertyId && f.ArchivedAt == null).ToListAsync(cancellationToken);
        var target = await db.FeeTypes.Include(f => f.Prices)
            .Where(f => f.PropertyId == request.TargetPropertyId && f.ArchivedAt == null).ToListAsync(cancellationToken);

        var from = request.EffectiveFrom ?? clock.GetUtcNow().ToBusinessDate();
        var skipped = new List<string>();
        var copied = 0;
        foreach (var fee in source.OrderBy(f => f.SortOrder))
        {
            var existing = target.FirstOrDefault(t => t.NameNormalized == fee.NameNormalized
                || (fee.SystemCode != null && t.SystemCode == fee.SystemCode));
            var price = request.IncludePrices ? fee.ResolvePrice(from) : null;

            // Điện / Nước mặc định của khu đích chưa có giá ⇒ chép giá vào khoản sẵn có thay vì bỏ qua.
            if (existing is not null)
            {
                if (price is not null && existing.SystemCode is not null && existing.SystemCode == fee.SystemCode && existing.Prices.Count == 0)
                {
                    existing.AddPrice(from, price.UnitPrice, price.Note, null, price.Tiers);
                    copied++;
                }
                else
                    skipped.Add(fee.Name);
                continue;
            }

            var clone = FeeType.Create(request.TargetPropertyId, fee.Name, fee.Group, fee.ChargeBasis, fee.Unit, fee.AutoAttach,
                fee.DefaultQuantity, fee.SortOrder, fee.SystemCode, fee.VehicleType);
            if (price is not null)
                clone.AddPrice(from, price.UnitPrice, price.Note, null, price.Tiers);
            db.FeeTypes.Add(clone);
            copied++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new CopyFeeCatalogResult(copied, skipped);
    }
}

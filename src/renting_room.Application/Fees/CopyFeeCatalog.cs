using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Fees;

// Sao chép danh mục (FE-UC-06)
public sealed record CopyFeeCatalogCommand(Guid SourcePropertyId, Guid TargetPropertyId, bool IncludePrices, DateOnly? EffectiveFrom)
    : IRequest<Result<CopyFeeCatalogResult>>;

public sealed record CopyFeeCatalogResult(int Copied, IReadOnlyList<string> Skipped);

/// <summary>
/// Chép khoản thu chưa ngừng dùng từ khu nguồn sang khu đích; trùng tên / trùng mã hệ thống (điện, nước) thì bỏ qua.
/// Kèm giá: chép giá đang hiệu lực, hiệu lực từ <c>effectiveFrom</c> (mặc định hôm nay).
/// </summary>
public sealed class CopyFeeCatalogHandler(IPropertyQueries properties, IFeeTypeRepository fees, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<CopyFeeCatalogCommand, Result<CopyFeeCatalogResult>>
{
    public async ValueTask<Result<CopyFeeCatalogResult>> Handle(CopyFeeCatalogCommand request, CancellationToken cancellationToken)
    {
        var ids = new[] { request.SourcePropertyId, request.TargetPropertyId };
        var statuses = await properties.GetStatusesAsync(ids, cancellationToken);
        if (statuses.Count != ids.Distinct().Count() || request.SourcePropertyId == request.TargetPropertyId)
            return PropertyErrors.PropertyNotFound;
        if (statuses.Single(p => p.Id == request.TargetPropertyId).IsArchived)
            return PropertyErrors.PropertyArchived;

        var source = await fees.ListActiveByPropertyAsync(request.SourcePropertyId, cancellationToken);
        var target = await fees.ListActiveByPropertyAsync(request.TargetPropertyId, cancellationToken);

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
            fees.Add(clone);
            copied++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CopyFeeCatalogResult(copied, skipped);
    }
}

using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Renters;

// Queries
/// <param name="PropertyId">Người từng đứng tên / ở trong HĐ của khu này.</param>
/// <param name="Staying"><c>true</c> = đang ở trong HĐ hiệu lực / đang thanh lý; <c>false</c> = không còn ở.</param>
/// <param name="IncludeArchived">Kèm hồ sơ đã ẩn (RT-BR-05).</param>
public sealed record SearchRentersQuery(
    string? Q, string? IdNumber, IdDocumentType? IdType, int Page = 1, int PageSize = Paging.DefaultPageSize, Guid? PropertyId = null,
    bool? Staying = null, bool IncludeArchived = false)
    : IRequest<PagedResult<RenterDto>>;

public sealed class SearchRentersQueryValidator : AbstractValidator<SearchRentersQuery>
{
    public SearchRentersQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Q).OptionalText(100);
        RuleFor(x => x.IdNumber).OptionalText(30);
    }
}

/// <summary>Tìm theo tên (không dấu) / SĐT; số giấy tờ tìm CHÍNH XÁC qua hash (không quét dữ liệu đã mã hóa).</summary>
public sealed class SearchRentersHandler(IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<SearchRentersQuery, PagedResult<RenterDto>>
{
    public async ValueTask<PagedResult<RenterDto>> Handle(SearchRentersQuery request, CancellationToken cancellationToken)
    {
        var query = db.Renters.AsNoTracking();
        if (!request.IncludeArchived)
            query = query.Where(r => r.ArchivedAt == null);
        if (request.PropertyId is { } propertyId)
            query = query.Where(r => db.Contracts.Any(c => c.PropertyId == propertyId && c.Status != ContractStatus.Cancelled
                && (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id))));
        if (request.Staying is { } staying)
        {
            var today = clock.GetUtcNow().ToBusinessDate();
            query = query.Where(r => db.Contracts.Any(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && c.Occupants.Any(o => o.RenterId == r.Id && o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today))) == staying);
        }

        if (!string.IsNullOrWhiteSpace(request.IdNumber))
        {
            // Hash gồm cả loại giấy tờ ⇒ không chọn loại thì thử mọi loại (hộ chiếu người nước ngoài vẫn tìm được).
            var types = request.IdType is { } type ? [type] : Enum.GetValues<IdDocumentType>();
            var hashes = types
                .Select(t => (string?)protector.ProtectIdNumber(currentUser.OrganizationId!.Value, t, request.IdNumber).Hash)
                .ToList();
            query = query.Where(r => hashes.Contains(r.IdNumberHash));
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var text = TextNormalizer.ToSearchText(request.Q);
            var phone = RenterProjection.NormalizePhone(request.Q);
            query = query.Where(r => r.FullNameSearch.Contains(text) || (phone != null && r.Phone == phone));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(r => r.FullNameSearch)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToDto()
            .ToListAsync(cancellationToken);

        return new PagedResult<RenterDto>(items, request.Page, request.PageSize, total);
    }
}

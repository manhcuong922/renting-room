using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;

namespace renting_room.Application.Properties;

public sealed record ListPropertiesQuery(string? Search, bool IncludeArchived, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<PropertySummaryDto>>;

public sealed class ListPropertiesQueryValidator : AbstractValidator<ListPropertiesQuery>
{
    public ListPropertiesQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Search).OptionalText(100);
    }
}

public sealed class ListPropertiesHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ListPropertiesQuery, PagedResult<PropertySummaryDto>>
{
    public async ValueTask<PagedResult<PropertySummaryDto>> Handle(ListPropertiesQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var query = db.Properties.AsNoTracking();
        if (!request.IncludeArchived)
            query = query.Where(p => p.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpper();
            query = query.Where(p => p.Code.Contains(term) || p.Name.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        // PR-BR-02: số phòng đang thuê đếm cùng điều kiện với trạng thái phòng (ngày chuyển phòng, ngày trả phòng vẫn tính).
        var occupied = db.OccupiedRoomIds(today);
        var rows = await query
            .OrderBy(p => p.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new
            {
                Property = p,
                RoomCount = db.Rooms.Count(r => r.PropertyId == p.Id && r.ArchivedAt == null),
                OccupiedRoomCount = db.Rooms.Count(r => r.PropertyId == p.Id && occupied.Contains(r.Id))
            })
            .ToListAsync(cancellationToken);

        // PR-BR-17: khu không khai bên cho thuê riêng ⇒ xét thông tin chủ trọ.
        var organizationLessorComplete = rows.Count > 0
            && (await LessorSource.OrganizationLessorAsync(db, rows[0].Property.OrganizationId, cancellationToken))?.IsComplete(today) == true;
        var items = rows.Select(r => new PropertySummaryDto(
                r.Property.Id, r.Property.Code, r.Property.Name, r.Property.Address.FullText, r.RoomCount, r.OccupiedRoomCount,
                r.Property.Lessor?.IsComplete(today) ?? organizationLessorComplete, r.Property.IsArchived))
            .ToList();

        return new PagedResult<PropertySummaryDto>(items, request.Page, request.PageSize, total);
    }
}

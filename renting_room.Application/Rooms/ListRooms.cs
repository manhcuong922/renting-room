using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record ListRoomsQuery(
    Guid? PropertyId,
    RoomDisplayStatus? Status,
    string? Floor,
    Guid? GroupId,
    string? Search,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    bool? Overdue = null) : IRequest<PagedResult<RoomDto>>;

public sealed class ListRoomsQueryValidator : AbstractValidator<ListRoomsQuery>
{
    public ListRoomsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Search).OptionalText(50);
        RuleFor(x => x.Floor).OptionalText(10);
    }
}

public sealed class ListRoomsHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ListRoomsQuery, PagedResult<RoomDto>>
{
    public async ValueTask<PagedResult<RoomDto>> Handle(ListRoomsQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var rooms = db.Rooms.AsNoTracking();

        if (request.PropertyId is { } propertyId)
            rooms = rooms.Where(r => r.PropertyId == propertyId);
        if (request.Status != RoomDisplayStatus.Archived)
            rooms = rooms.Where(r => r.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Floor))
            rooms = rooms.Where(r => r.Floor == request.Floor.Trim());
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = TextNormalizer.NormalizeCode(request.Search);
            rooms = rooms.Where(r => r.Code.Contains(term));
        }
        if (request.GroupId is { } groupId)
            rooms = rooms.Where(r => db.RoomGroups.Any(g => g.Id == groupId && g.Members.Any(m => m.RoomId == r.Id)));

        var query = rooms.ToRows(db, today);
        if (request.Status is { } status)
            query = query.Where(r => r.Status == status);
        // PR-BR-16: bộ lọc "Quá hạn" — phòng có phiếu quá hạn thanh toán chưa thu đủ.
        if (request.Overdue is { } overdue)
            query = query.Where(r => (r.OverdueAmount > 0) == overdue);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(r => r.PropertyCode).ThenBy(r => r.Room.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RoomDto>(await rows.ToDtosAsync(db, today, cancellationToken), request.Page, request.PageSize, total);
    }
}

using FluentValidation;
using Mediator;
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

public sealed class ListRoomsHandler(IRoomQueries rooms, TimeProvider clock) : IRequestHandler<ListRoomsQuery, PagedResult<RoomDto>>
{
    public async ValueTask<PagedResult<RoomDto>> Handle(ListRoomsQuery request, CancellationToken cancellationToken) =>
        await rooms.ListAsync(request, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
}

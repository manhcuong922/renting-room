using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using renting_room.Application.Rooms.Commands.CreateRoom;
using renting_room.Application.Rooms.Queries.GetRoom;
using renting_room.Application.Rooms.Queries.ListRooms;
using renting_room.Errors;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

/// <summary>Scaffold phòng — chỉ thấy phòng của tổ chức mình (global query filter). Viết lại theo plan M02.</summary>
public static class RoomEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/rooms";

    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .WithTags("Rooms")
            .RequireAuthorization(AuthPolicies.OrgMember);

        group.MapGet("/", ListRooms).WithSummary("Danh sách phòng của tổ chức");
        group.MapGet("/{id:guid}", GetRoom).WithSummary("Chi tiết phòng");
        group.MapPost("/", CreateRoom)
            .WithIdempotency(required: true) // bấm 2 lần / retry mạng không tạo 2 phòng
            .WithSummary("Tạo phòng");
    }

    private static async Task<Ok<IReadOnlyList<RoomDto>>> ListRooms(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ListRoomsQuery(), cancellationToken));

    private static async Task<Results<Ok<RoomDto>, ProblemHttpResult>> GetRoom(
        Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRoomQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    private static async Task<Results<Created<Guid>, ProblemHttpResult>> CreateRoom(
        CreateRoomCommand command, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"{Route}/{result.Value}", result.Value)
            : result.Error!.ToProblem();
    }
}

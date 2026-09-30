using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using renting_room.Application.Rooms.Commands.CreateRoom;
using renting_room.Application.Rooms.Queries.GetRoom;
using renting_room.Application.Rooms.Queries.ListRooms;

namespace renting_room.Endpoints;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/rooms").WithTags("Rooms");

        group.MapGet("/", ListRooms)
            .WithName("ListRooms")
            .WithSummary("List all rooms")
            .Produces<IReadOnlyList<RoomDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetRoom)
            .WithName("GetRoom")
            .WithSummary("Get a room by ID")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateRoom)
            .WithName("CreateRoom")
            .WithSummary("Create a new room")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<Ok<IReadOnlyList<RoomDto>>> ListRooms(
        ISender sender, CancellationToken cancellationToken)
    {
        var rooms = await sender.Send(new ListRoomsQuery(), cancellationToken);
        return TypedResults.Ok(rooms);
    }

    private static async Task<Results<Ok<RoomDto>, NotFound>> GetRoom(
        Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRoomQuery(id), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<Guid>, ValidationProblem>> CreateRoom(
        CreateRoomCommand command, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/rooms/{result.Value}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["error"] = [result.Error ?? "Unable to create room."]
            });
    }
}

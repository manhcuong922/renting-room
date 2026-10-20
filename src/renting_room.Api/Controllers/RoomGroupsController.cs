using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Rooms;
using renting_room.Http;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Room groups")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class RoomGroupsController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/room-groups";

    [HttpGet("properties/{propertyId:guid}/room-groups")]
    [EndpointSummary("Danh sách nhóm phòng của khu")]
    public async Task<IResult> List(Guid propertyId, CancellationToken ct) =>
        (await sender.Send(new ListRoomGroupsQuery(propertyId), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/room-groups")]
    [EndpointSummary("Tạo nhóm phòng")]
    public async Task<IResult> Create(Guid propertyId, RoomGroupRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new SaveRoomGroupCommand(null, propertyId, body.Name, body.Description), ct);
        return result.IsSuccess ? Results.Created($"{Route}/{result.Value!.Id}", result.Value) : result.ToHttp();
    }

    [HttpPut("room-groups/{id:guid}")]
    [EndpointSummary("Đổi tên nhóm")]
    public async Task<IResult> Rename(Guid id, RoomGroupRequest body, CancellationToken ct) =>
        (await sender.Send(new SaveRoomGroupCommand(id, Guid.Empty, body.Name, body.Description), ct)).ToHttp();

    [HttpDelete("room-groups/{id:guid}")]
    [EndpointSummary("Xóa nhóm (không xóa phòng)")]
    public async Task<IResult> Delete(Guid id, CancellationToken ct) =>
        (await sender.Send(new DeleteRoomGroupCommand(id), ct)).ToHttp();

    [HttpPut("room-groups/{id:guid}/members")]
    [EndpointSummary("Đặt lại danh sách phòng của nhóm (phòng phải cùng khu)")]
    public async Task<IResult> SetMembers(Guid id, RoomGroupMembersRequest body, CancellationToken ct) =>
        (await sender.Send(new SetRoomGroupMembersCommand(id, body.RoomIds), ct)).ToHttp();
}

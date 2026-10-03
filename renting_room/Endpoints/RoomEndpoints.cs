using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Rooms;
using renting_room.Domain.Properties;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record CreateRoomRequest(string Code, RoomSpecInput Spec);

public sealed record BulkCreateRoomsRequest(
    IReadOnlyList<BulkRoomFloor> Floors,
    int MaxOccupants,
    decimal? AreaM2,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string>? Amenities);

public sealed record UpdateRoomRequest(string Code, RoomSpecInput Spec, uint Version);

public sealed record MaintenanceRequest(string? Note);

public sealed record RoomGroupRequest(string Name, string? Description);

public sealed record RoomGroupMembersRequest(IReadOnlyList<Guid> RoomIds);

/// <summary>Phòng và nhóm phòng (M02). Trạng thái phòng tính từ hợp đồng, không sửa trực tiếp.</summary>
public static class RoomEndpoints
{
    private const string RoomsRoute = $"{EndpointHelpers.ApiPrefix}/rooms";
    private const string PropertiesRoute = $"{EndpointHelpers.ApiPrefix}/properties";
    private const string GroupsRoute = $"{EndpointHelpers.ApiPrefix}/room-groups";

    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup(RoomsRoute).WithTags("Rooms").RequireAuthorization(AuthPolicies.OrgMember);
        var byProperty = app.MapGroup($"{PropertiesRoute}/{{propertyId:guid}}").WithTags("Rooms").RequireAuthorization(AuthPolicies.OrgMember);

        rooms.MapGet("/", async (
                Guid? propertyId, RoomDisplayStatus? status, string? floor, Guid? groupId, string? search, int? page, int? pageSize,
                ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListRoomsQuery(
                    propertyId, status, floor, groupId, search, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Danh sách phòng — lọc theo khu, trạng thái (Vacant/Reserved/Occupied/Maintenance/Archived), tầng, nhóm");

        rooms.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetRoomQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết phòng + hợp đồng hiện hành");

        byProperty.MapPost("/rooms", async (Guid propertyId, CreateRoomRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateRoomCommand(propertyId, body.Code, body.Spec), ct)).ToCreated(RoomsRoute))
            .WithIdempotency(required: true)
            .WithSummary("Tạo phòng");

        byProperty.MapPost("/rooms/bulk", async (Guid propertyId, BulkCreateRoomsRequest body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new BulkCreateRoomsCommand(
                    propertyId, body.Floors, body.MaxOccupants, body.AreaM2, body.ListedRent, body.DefaultDeposit, body.Amenities), ct);
                return result.IsSuccess ? Results.Created($"{RoomsRoute}?propertyId={propertyId}", result.Value) : result.ToHttp();
            })
            .WithIdempotency(required: true)
            .WithSummary("Tạo phòng hàng loạt theo tầng (mã trùng ⇒ không tạo phòng nào)");

        rooms.MapPut("/{id:guid}", async (Guid id, UpdateRoomRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateRoomCommand(id, body.Code, body.Spec, body.Version), ct)).ToHttp())
            .WithSummary("Sửa phòng");

        rooms.MapPost("/{id:guid}/maintenance/start", async (Guid id, MaintenanceRequest? body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.StartMaintenance, body?.Note), ct)).ToHttp())
            .WithSummary("Bắt đầu bảo trì (chỉ phòng không có hợp đồng hiệu lực)");
        rooms.MapPost("/{id:guid}/maintenance/end", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.EndMaintenance), ct)).ToHttp())
            .WithSummary("Kết thúc bảo trì");
        rooms.MapPost("/{id:guid}/archive", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.Archive), ct)).ToHttp())
            .WithSummary("Ngừng sử dụng phòng");
        rooms.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.Restore), ct)).ToHttp())
            .WithSummary("Khôi phục phòng");

        // ---- Nhóm phòng
        byProperty.MapGet("/room-groups", async (Guid propertyId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListRoomGroupsQuery(propertyId), ct)).ToHttp())
            .WithTags("Room groups")
            .WithSummary("Danh sách nhóm phòng của khu");

        byProperty.MapPost("/room-groups", async (Guid propertyId, RoomGroupRequest body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new SaveRoomGroupCommand(null, propertyId, body.Name, body.Description), ct);
                return result.IsSuccess ? Results.Created($"{GroupsRoute}/{result.Value!.Id}", result.Value) : result.ToHttp();
            })
            .WithTags("Room groups")
            .WithSummary("Tạo nhóm phòng");

        var groups = app.MapGroup(GroupsRoute).WithTags("Room groups").RequireAuthorization(AuthPolicies.OrgMember);
        groups.MapPut("/{id:guid}", async (Guid id, RoomGroupRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SaveRoomGroupCommand(id, Guid.Empty, body.Name, body.Description), ct)).ToHttp())
            .WithSummary("Đổi tên nhóm");
        groups.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new DeleteRoomGroupCommand(id), ct)).ToHttp())
            .WithSummary("Xóa nhóm (không xóa phòng)");
        groups.MapPut("/{id:guid}/members", async (Guid id, RoomGroupMembersRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetRoomGroupMembersCommand(id, body.RoomIds), ct)).ToHttp())
            .WithSummary("Đặt lại danh sách phòng của nhóm (phòng phải cùng khu)");
    }
}

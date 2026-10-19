using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using renting_room.Application.Common.Models;
using renting_room.Application.Contracts;
using renting_room.Application.Rooms;
using renting_room.Domain.Properties;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>Phòng (M02). Trạng thái phòng tính từ hợp đồng, không sửa trực tiếp.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Rooms")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class RoomsController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/rooms";

    [HttpGet("rooms")]
    [EndpointSummary("Danh sách phòng — lọc theo khu, trạng thái (Vacant/Reserved/Occupied/Maintenance/Archived), tầng, nhóm, quá hạn thanh toán")]
    public async Task<IResult> List(
        Guid? propertyId, RoomDisplayStatus? status, string? floor, Guid? groupId, string? search, int? page, int? pageSize, bool? overdue,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListRoomsQuery(
            propertyId, status, floor, groupId, search, page ?? 1, pageSize ?? Paging.DefaultPageSize, overdue), ct));

    [HttpGet("rooms/{id:guid}/tenancy-history")]
    [EndpointSummary("Lịch sử thuê của phòng: từng lần một HĐ ở phòng, người đứng tên, người ở và vai trò (mới nhất trước)")]
    public async Task<IResult> GetTenancyHistory(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetRoomTenancyHistoryQuery(id), ct)).ToHttp();

    [HttpGet("tenancy-history")]
    [EndpointSummary("Tìm theo họ tên: người đó đã ở / đứng tên phòng nào, HĐ nào, từ – đến")]
    public async Task<IResult> SearchTenancyHistory(string? search, Guid? propertyId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SearchTenancyHistoryQuery(search ?? string.Empty, propertyId), ct));

    [HttpGet("rooms/{id:guid}")]
    [EndpointSummary("Chi tiết phòng + hợp đồng hiện hành")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetRoomQuery(id), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/rooms")]
    [Idempotent]
    [EndpointSummary("Tạo phòng")]
    public async Task<IResult> Create(Guid propertyId, CreateRoomRequest body, CancellationToken ct) =>
        (await sender.Send(new CreateRoomCommand(propertyId, body.Code, body.Spec), ct)).ToCreated(Route);

    [HttpPost("properties/{propertyId:guid}/rooms/bulk")]
    [Idempotent]
    [EndpointSummary("Tạo phòng hàng loạt theo tầng (mã trùng ⇒ không tạo phòng nào)")]
    public async Task<IResult> BulkCreate(Guid propertyId, BulkCreateRoomsRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new BulkCreateRoomsCommand(
            propertyId, body.Floors, body.MaxOccupants, body.AreaM2, body.ListedRent, body.DefaultDeposit, body.Amenities), ct);
        return result.IsSuccess ? Results.Created($"{Route}?propertyId={propertyId}", result.Value) : result.ToHttp();
    }

    [HttpPut("rooms/{id:guid}")]
    [EndpointSummary("Sửa phòng")]
    public async Task<IResult> Update(Guid id, UpdateRoomRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateRoomCommand(id, body.Code, body.Spec, body.Version), ct)).ToHttp();

    [HttpPost("rooms/{id:guid}/maintenance/start")]
    [EndpointSummary("Bắt đầu bảo trì (chỉ phòng không có hợp đồng hiệu lực)")]
    public async Task<IResult> StartMaintenance(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MaintenanceRequest? body, CancellationToken ct) =>
        (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.StartMaintenance, body?.Note), ct)).ToHttp();

    [HttpPost("rooms/{id:guid}/maintenance/end")]
    [EndpointSummary("Kết thúc bảo trì")]
    public async Task<IResult> EndMaintenance(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.EndMaintenance), ct)).ToHttp();

    [HttpPost("rooms/{id:guid}/apply-listed-rent")]
    [EndpointSummary("Áp giá niêm yết của phòng cho người đang thuê — từ kỳ chưa chốt đầu tiên (hỏi sau khi sửa giá phòng)")]
    public async Task<IResult> ApplyListedRent(Guid id, CancellationToken ct) =>
        (await sender.Send(new ApplyListedRentCommand(id), ct)).ToHttp();

    [HttpPost("rooms/{id:guid}/archive")]
    [EndpointSummary("Ngừng sử dụng phòng")]
    public async Task<IResult> Archive(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.Archive), ct)).ToHttp();

    [HttpPost("rooms/{id:guid}/restore")]
    [EndpointSummary("Khôi phục phòng")]
    public async Task<IResult> Restore(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeRoomStateCommand(id, RoomAction.Restore), ct)).ToHttp();
}

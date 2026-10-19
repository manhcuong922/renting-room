using Mediator;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Billing;
using renting_room.Domain.Contracts;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;

namespace renting_room.Controllers;

/// <summary>Khoản phát sinh theo phòng (M07 BL-UC-15..17): phụ thu / bù tạo ngay lúc xảy ra, cuối tháng tự vào phiếu.</summary>
[ApiController]
[Route("api/v1")]
[Tags("RoomCharges")]
[Consumes("application/json")]
public sealed class RoomChargesController(ISender sender) : ControllerBase
{
    [HttpGet("rooms/{roomId:guid}/charges")]
    [EndpointSummary("Khoản phát sinh của phòng (chờ vào phiếu / trên nháp / đã vào phiếu / đã hủy)")]
    public async Task<IResult> ListByRoom(Guid roomId, RoomChargeStatus? status, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListRoomChargesQuery(roomId, null, status, null, null), ct));

    [HttpPost("rooms/{roomId:guid}/charges")]
    [Idempotent]
    [EndpointSummary("Tạo phụ thu / bù cho phòng đang có người thuê — đã thanh toán hay chưa; tự vào phiếu của kỳ chứa ngày phát sinh")]
    public async Task<IResult> Create(Guid roomId, CreateRoomChargeRequest b, CancellationToken ct)
    {
        var settlement = b.Settled == true ? new ChargeSettlement(b.SettledOn ?? b.IncurredOn, b.Method ?? PaymentMethod.Cash) : null;
        var result = await sender.Send(
            new CreateRoomChargeCommand(roomId, b.Kind, b.Description, b.Amount, b.IncurredOn, b.Reason, settlement), ct);
        return result.IsSuccess ? Results.Created($"/api/v1/room-charges/{result.Value!.Id}", result.Value) : result.ToHttp();
    }

    [HttpGet("room-charges")]
    [EndpointSummary("Khoản phát sinh theo khu / phòng / trạng thái / khoảng ngày")]
    public async Task<IResult> List(Guid? propertyId, Guid? roomId, RoomChargeStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListRoomChargesQuery(roomId, propertyId, status, from, to), ct));

    [HttpPost("room-charges/{id:guid}/settle")]
    [EndpointSummary("Đánh dấu khoản đã thanh toán / đã hoàn ngay (chưa nằm trên phiếu đã chốt) — dòng trên nháp ra khỏi tổng")]
    public async Task<IResult> Settle(Guid id, SettleRoomChargeRequest b, CancellationToken ct) =>
        (await sender.Send(new SettleRoomChargeCommand(id, b.SettledOn, b.Method), ct)).ToHttp();

    [HttpDelete("room-charges/{id:guid}/settle")]
    [EndpointSummary("Bỏ đánh dấu đã thanh toán (nhập nhầm)")]
    public async Task<IResult> Unsettle(Guid id, CancellationToken ct) =>
        (await sender.Send(new UnsettleRoomChargeCommand(id), ct)).ToHttp();

    [HttpPost("room-charges/{id:guid}/cancel")]
    [EndpointSummary("Hủy khoản phát sinh (chưa nằm trên phiếu đã chốt)")]
    public async Task<IResult> Cancel(Guid id, CancelRoomChargeRequest b, CancellationToken ct) =>
        (await sender.Send(new CancelRoomChargeCommand(id, b.Reason), ct)).ToHttp();
}

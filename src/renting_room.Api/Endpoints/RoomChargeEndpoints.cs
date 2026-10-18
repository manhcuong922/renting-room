using Mediator;
using renting_room.Application.Billing;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Idempotency;

namespace renting_room.Endpoints;

/// <param name="Settled">true ⇒ đã thanh toán (phụ thu) / đã hoàn (bù) ngay — cần <paramref name="Method"/>; <paramref name="SettledOn"/> bỏ trống = ngày phát sinh.</param>
public sealed record CreateRoomChargeRequest(
    RoomChargeKind Kind, string Description, decimal Amount, DateOnly IncurredOn, string Reason, bool? Settled, DateOnly? SettledOn,
    PaymentMethod? Method);

public sealed record SettleRoomChargeRequest(DateOnly SettledOn, PaymentMethod Method);

public sealed record CancelRoomChargeRequest(string Reason);

/// <summary>Khoản phát sinh theo phòng (M07 BL-UC-15..17): phụ thu / bù tạo ngay lúc xảy ra, cuối tháng tự vào phiếu.</summary>
public static class RoomChargeEndpoints
{
    public static void MapRoomChargeEndpoints(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup($"{EndpointHelpers.ApiPrefix}/rooms/{{roomId:guid}}/charges").WithTags("RoomCharges");
        rooms.MapGet("/", async (Guid roomId, RoomChargeStatus? status, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListRoomChargesQuery(roomId, null, status, null, null), ct)))
            .WithSummary("Khoản phát sinh của phòng (chờ vào phiếu / trên nháp / đã vào phiếu / đã hủy)");
        rooms.MapPost("/", async (Guid roomId, CreateRoomChargeRequest b, ISender sender, CancellationToken ct) =>
            {
                var settlement = b.Settled == true ? new ChargeSettlement(b.SettledOn ?? b.IncurredOn, b.Method ?? PaymentMethod.Cash) : null;
                var result = await sender.Send(
                    new CreateRoomChargeCommand(roomId, b.Kind, b.Description, b.Amount, b.IncurredOn, b.Reason, settlement), ct);
                return result.IsSuccess ? Results.Created($"{EndpointHelpers.ApiPrefix}/room-charges/{result.Value!.Id}", result.Value) : result.ToHttp();
            })
            .WithIdempotency(required: true)
            .WithSummary("Tạo phụ thu / bù cho phòng đang có người thuê — đã thanh toán hay chưa; tự vào phiếu của kỳ chứa ngày phát sinh");

        var charges = app.MapGroup($"{EndpointHelpers.ApiPrefix}/room-charges").WithTags("RoomCharges");
        charges.MapGet("/", async (Guid? propertyId, Guid? roomId, RoomChargeStatus? status, DateOnly? from, DateOnly? to, ISender sender,
                    CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListRoomChargesQuery(roomId, propertyId, status, from, to), ct)))
            .WithSummary("Khoản phát sinh theo khu / phòng / trạng thái / khoảng ngày");
        charges.MapPost("/{id:guid}/settle", async (Guid id, SettleRoomChargeRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SettleRoomChargeCommand(id, b.SettledOn, b.Method), ct)).ToHttp())
            .WithSummary("Đánh dấu khoản đã thanh toán / đã hoàn ngay (chưa nằm trên phiếu đã chốt) — dòng trên nháp ra khỏi tổng");
        charges.MapDelete("/{id:guid}/settle", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UnsettleRoomChargeCommand(id), ct)).ToHttp())
            .WithSummary("Bỏ đánh dấu đã thanh toán (nhập nhầm)");
        charges.MapPost("/{id:guid}/cancel", async (Guid id, CancelRoomChargeRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CancelRoomChargeCommand(id, b.Reason), ct)).ToHttp())
            .WithSummary("Hủy khoản phát sinh (chưa nằm trên phiếu đã chốt)");
    }
}

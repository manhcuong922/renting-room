using Mediator;
using renting_room.Application.Meters;
using renting_room.Idempotency;

namespace renting_room.Endpoints;

public static class MeterEndpoints
{
    private const string MetersRoute = $"{EndpointHelpers.ApiPrefix}/meters";

    public static void MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup($"{EndpointHelpers.ApiPrefix}/rooms/{{roomId:guid}}/meters").WithTags("Meters");
        rooms.MapGet("/", async (Guid roomId, bool? includeRemoved, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListRoomMetersQuery(roomId, includeRemoved ?? false), ct)).ToHttp())
            .WithSummary("Công tơ của phòng kèm chỉ số mới nhất");
        rooms.MapPost("/", async (Guid roomId, InstallMeterRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new InstallMeterCommand(roomId, b.FeeTypeId, b.SerialNo, b.InstalledDate, b.InitialValue, b.Note), ct))
                .ToCreated(MetersRoute))
            .WithIdempotency(required: true)
            .WithSummary("Lắp công tơ cho phòng (điện / nước theo công tơ) kèm chỉ số ban đầu");

        // MT-UC-08 (M2): thay công tơ hàng loạt giữa tháng (điện lực thay cả khu).
        var properties = app.MapGroup($"{EndpointHelpers.ApiPrefix}/properties/{{propertyId:guid}}/meters").WithTags("Meters");
        properties.MapGet("/replace-sheet", async (Guid propertyId, Guid feeTypeId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetReplaceSheetQuery(propertyId, feeTypeId), ct)).ToHttp())
            .WithSummary("Lưới thay công tơ hàng loạt: công tơ đang hoạt động của khoản thu trong khu, chỉ số gần nhất");
        properties.MapGet("/install-sheet", async (Guid propertyId, Guid feeTypeId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetInstallSheetQuery(propertyId, feeTypeId), ct)).ToHttp())
            .WithSummary("Lưới lắp công tơ hàng loạt: các phòng của khu chưa có công tơ đang hoạt động của khoản thu");
        properties.MapPost("/bulk", async (Guid propertyId, BulkInstallMetersRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new BulkInstallMetersCommand(propertyId, b.FeeTypeId, b.InstalledDate, b.Rows), ct)).ToHttp())
            .WithIdempotency(required: true)
            .WithSummary("Lắp công tơ hàng loạt cho nhiều phòng của khu — lỗi 1 dòng ⇒ không lưu dòng nào (rowErrors)");
        properties.MapGet("/vacant-usage", async (Guid propertyId, DateOnly? from, DateOnly? to, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetVacantUsageQuery(propertyId, from, to), ct)).ToHttp())
            .WithSummary("Sản lượng điện nước lúc phòng trống (từ chỉ số liền trước tới chỉ số nhận phòng) — để đối chiếu hóa đơn của khu");
        properties.MapPost("/bulk-replace", async (Guid propertyId, BulkReplaceMetersRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new BulkReplaceMetersCommand(propertyId, b.FeeTypeId, b.ReplacedOn, b.Rows, b.Note), ct)).ToHttp())
            .WithIdempotency(required: true)
            .WithSummary("Thay công tơ hàng loạt cùng 1 ngày — lỗi 1 dòng ⇒ không lưu dòng nào (rowErrors)");

        var meters = app.MapGroup(MetersRoute).WithTags("Meters");
        meters.MapPost("/{id:guid}/replace", async (Guid id, ReplaceMeterRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ReplaceMeterCommand(id, b.Date, b.OldFinalValue, b.NewSerialNo, b.NewInitialValue, b.Note), ct))
                .ToCreated(MetersRoute))
            .WithIdempotency(required: true)
            .WithSummary("Thay công tơ: số cuối công tơ cũ + số ban đầu công tơ mới (cùng ngày)");
        meters.MapPost("/{id:guid}/remove", async (Guid id, RemoveMeterRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemoveMeterCommand(id, b.Date, b.FinalValue, b.Note), ct)).ToHttp())
            .WithSummary("Tháo công tơ (không thay) — bắt buộc số cuối");
        meters.MapGet("/{id:guid}/readings", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListMeterReadingsQuery(id), ct)).ToHttp())
            .WithSummary("Lịch sử chỉ số (mới nhất trước)");

        var readings = app.MapGroup($"{EndpointHelpers.ApiPrefix}/meter-readings").WithTags("Meters");
        readings.MapPut("/{id:guid}", async (Guid id, CorrectReadingRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CorrectMeterReadingCommand(id, b.Value, b.Note), ct)).ToHttp())
            .WithSummary("Sửa chỉ số nhập sai (vẫn phải ≥ chỉ số trước, ≤ chỉ số sau)");
        readings.MapPost("/{id:guid}/void", async (Guid id, VoidReadingRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new VoidMeterReadingCommand(id, b.Reason), ct)).ToHttp())
            .WithSummary("Hủy chỉ số ghi nhầm / ghi thừa (chỉ chỉ số cuối kỳ, kiểm tra; chưa bị phiếu đã chốt khóa)");
        readings.MapPost("/{id:guid}/adjust", async (Guid id, AdjustReadingRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AdjustMeterReadingCommand(id, b.Value, b.Reason), ct)).ToHttp())
            .WithIdempotency(required: true)
            .WithSummary("Điều chỉnh chỉ số đã chốt (gõ sai): sửa chỉ số + tự tạo khoản bù / phụ thu phần tiền chênh vào phiếu kỳ sau");
    }
}

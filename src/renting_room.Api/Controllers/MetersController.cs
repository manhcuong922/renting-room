using Mediator;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Meters;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Meters")]
[Consumes("application/json")]
public sealed class MetersController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/meters";

    [HttpGet("rooms/{roomId:guid}/meters")]
    [EndpointSummary("Công tơ của phòng kèm chỉ số mới nhất")]
    public async Task<IResult> ListByRoom(Guid roomId, bool? includeRemoved, CancellationToken ct) =>
        (await sender.Send(new ListRoomMetersQuery(roomId, includeRemoved ?? false), ct)).ToHttp();

    [HttpPost("rooms/{roomId:guid}/meters")]
    [Idempotent]
    [EndpointSummary("Lắp công tơ cho phòng (điện / nước theo công tơ) kèm chỉ số ban đầu")]
    public async Task<IResult> Install(Guid roomId, InstallMeterRequest b, CancellationToken ct) =>
        (await sender.Send(new InstallMeterCommand(roomId, b.FeeTypeId, b.SerialNo, b.InstalledDate, b.InitialValue, b.Note), ct))
        .ToCreated(Route);

    [HttpGet("properties/{propertyId:guid}/meters/replace-sheet")]
    [EndpointSummary("Lưới thay công tơ hàng loạt: công tơ đang hoạt động của khoản thu trong khu, chỉ số gần nhất")]
    public async Task<IResult> GetReplaceSheet(Guid propertyId, Guid feeTypeId, CancellationToken ct) =>
        (await sender.Send(new GetReplaceSheetQuery(propertyId, feeTypeId), ct)).ToHttp();

    [HttpGet("properties/{propertyId:guid}/meters/install-sheet")]
    [EndpointSummary("Lưới lắp công tơ hàng loạt: các phòng của khu chưa có công tơ đang hoạt động của khoản thu")]
    public async Task<IResult> GetInstallSheet(Guid propertyId, Guid feeTypeId, CancellationToken ct) =>
        (await sender.Send(new GetInstallSheetQuery(propertyId, feeTypeId), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/meters/bulk")]
    [Idempotent]
    [EndpointSummary("Lắp công tơ hàng loạt cho nhiều phòng của khu — lỗi 1 dòng ⇒ không lưu dòng nào (rowErrors)")]
    public async Task<IResult> BulkInstall(Guid propertyId, BulkInstallMetersRequest b, CancellationToken ct) =>
        (await sender.Send(new BulkInstallMetersCommand(propertyId, b.FeeTypeId, b.InstalledDate, b.Rows), ct)).ToHttp();

    [HttpGet("properties/{propertyId:guid}/meters/vacant-usage")]
    [EndpointSummary("Sản lượng điện nước lúc phòng trống (từ chỉ số liền trước tới chỉ số nhận phòng) — để đối chiếu hóa đơn của khu")]
    public async Task<IResult> GetVacantUsage(Guid propertyId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        (await sender.Send(new GetVacantUsageQuery(propertyId, from, to), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/meters/bulk-replace")]
    [Idempotent]
    [EndpointSummary("Thay công tơ hàng loạt cùng 1 ngày — lỗi 1 dòng ⇒ không lưu dòng nào (rowErrors)")]
    public async Task<IResult> BulkReplace(Guid propertyId, BulkReplaceMetersRequest b, CancellationToken ct) =>
        (await sender.Send(new BulkReplaceMetersCommand(propertyId, b.FeeTypeId, b.ReplacedOn, b.Rows, b.Note), ct)).ToHttp();

    [HttpGet("properties/{propertyId:guid}/meter-reading-sheet")]
    [EndpointSummary("Lưới ghi chỉ số cuối kỳ của khu theo tháng thu (lọc tầng, khoản thu)")]
    public async Task<IResult> GetReadingSheet(Guid propertyId, string billingMonth, string? floor, Guid? feeTypeId, CancellationToken ct) =>
        (await sender.Send(new GetMeterReadingSheetQuery(propertyId, billingMonth, floor, feeTypeId), ct)).ToHttp();

    [HttpPut("properties/{propertyId:guid}/meter-readings")]
    [Idempotent(required: false)]
    [EndpointSummary("Lưu chỉ số cuối kỳ hàng loạt (lỗi 1 dòng ⇒ không lưu dòng nào)")]
    public async Task<IResult> SaveReadings(Guid propertyId, SaveMeterReadingsRequest b, CancellationToken ct) =>
        (await sender.Send(new SaveMeterReadingsCommand(propertyId, b.Readings), ct)).ToHttp();

    [HttpPost("meters/{id:guid}/replace")]
    [Idempotent]
    [EndpointSummary("Thay công tơ: số cuối công tơ cũ + số ban đầu công tơ mới (cùng ngày)")]
    public async Task<IResult> Replace(Guid id, ReplaceMeterRequest b, CancellationToken ct) =>
        (await sender.Send(new ReplaceMeterCommand(id, b.Date, b.OldFinalValue, b.NewSerialNo, b.NewInitialValue, b.Note), ct))
        .ToCreated(Route);

    [HttpPost("meters/{id:guid}/remove")]
    [EndpointSummary("Tháo công tơ (không thay) — bắt buộc số cuối")]
    public async Task<IResult> Remove(Guid id, RemoveMeterRequest b, CancellationToken ct) =>
        (await sender.Send(new RemoveMeterCommand(id, b.Date, b.FinalValue, b.Note), ct)).ToHttp();

    [HttpGet("meters/{id:guid}/readings")]
    [EndpointSummary("Lịch sử chỉ số (mới nhất trước)")]
    public async Task<IResult> ListReadings(Guid id, CancellationToken ct) =>
        (await sender.Send(new ListMeterReadingsQuery(id), ct)).ToHttp();

    [HttpPut("meter-readings/{id:guid}")]
    [EndpointSummary("Sửa chỉ số nhập sai (vẫn phải ≥ chỉ số trước, ≤ chỉ số sau)")]
    public async Task<IResult> CorrectReading(Guid id, CorrectReadingRequest b, CancellationToken ct) =>
        (await sender.Send(new CorrectMeterReadingCommand(id, b.Value, b.Note), ct)).ToHttp();

    [HttpPost("meter-readings/{id:guid}/void")]
    [EndpointSummary("Hủy chỉ số ghi nhầm / ghi thừa (chỉ chỉ số cuối kỳ, kiểm tra; chưa bị phiếu đã chốt khóa)")]
    public async Task<IResult> VoidReading(Guid id, VoidReadingRequest b, CancellationToken ct) =>
        (await sender.Send(new VoidMeterReadingCommand(id, b.Reason), ct)).ToHttp();

    [HttpPost("meter-readings/{id:guid}/adjust")]
    [Idempotent]
    [EndpointSummary("Điều chỉnh chỉ số đã chốt (gõ sai): hóa đơn chưa thu ⇒ tính lại trên hóa đơn; đã thu ⇒ Xử lý ngay (tính lại + hoàn / thu thêm) hoặc Đẩy sang hóa đơn sau")]
    public async Task<IResult> AdjustReading(Guid id, AdjustReadingRequest b, CancellationToken ct) =>
        (await sender.Send(new AdjustMeterReadingCommand(id, b.Value, b.Reason, b.Handling, b.Settlement, b.NotReturned ?? false), ct)).ToHttp();
}

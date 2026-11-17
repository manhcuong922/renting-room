using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Notifications;
using renting_room.Application.Properties;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>M11 (docs/plans/11-notification.md, docs/api/notifications.md): gửi phiếu / nhắc nợ qua Zalo Bot và email.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Notifications")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class NotificationsController(ISender sender) : ControllerBase
{
    [HttpGet("notifications/channels")]
    [EndpointSummary("Kênh nào dùng được: Zalo (đã cấu hình token chưa + tên bot), Email (đã cấu hình SMTP chưa)")]
    public async Task<IResult> Channels(CancellationToken ct) => Results.Ok(await sender.Send(new GetNotificationChannelsQuery(), ct));

    [HttpGet("notifications/summary")]
    [EndpointSummary("Tổng hợp tin theo khu + tháng thu (billingMonth là ngày đầu tháng): đang chờ / đã gửi / lỗi / bỏ qua / hủy / phiếu chưa có kênh")]
    public async Task<IResult> Summary(Guid propertyId, DateOnly billingMonth, CancellationToken ct) =>
        (await sender.Send(new GetNotificationSummaryQuery(propertyId, billingMonth), ct)).ToHttp();

    [HttpGet("invoices/{id:guid}/notifications")]
    [EndpointSummary("Lịch sử gửi của một phiếu (kênh, loại, trạng thái, số lần thử, lỗi)")]
    public async Task<IResult> ListForInvoice(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetInvoiceNotificationsQuery(id), ct)).ToHttp();

    [HttpPost("invoices/{id:guid}/notifications/resend")]
    [Idempotent(required: false)]
    [EndpointSummary("Gửi lại phiếu đã chốt cho người đứng tên (mọi kênh họ có, hoặc chỉ các kênh chọn); không bị giới hạn khung giờ")]
    public async Task<IResult> Resend(Guid id, ResendNotificationRequest? body, CancellationToken ct)
    {
        var result = await sender.Send(new ResendInvoiceNotificationCommand(id, body?.Channels), ct);
        return result.IsSuccess ? Results.Accepted(value: result.Value) : result.ToHttp();
    }

    [HttpPost("renters/{id:guid}/zalo-link-code")]
    [EndpointSummary("Tạo mã liên kết Zalo 8 ký tự (hạn 48 giờ, dùng một lần; mã cũ chưa dùng hết hiệu lực). Mã thô chỉ trả một lần")]
    public async Task<IResult> CreateLinkCode(Guid id, CancellationToken ct) =>
        (await sender.Send(new CreateZaloLinkCodeCommand(id), ct)).ToHttp();

    [HttpDelete("renters/{id:guid}/zalo-link")]
    [EndpointSummary("Hủy liên kết Zalo của người thuê; tin Zalo đang chờ của người đó bị hủy")]
    public async Task<IResult> Unlink(Guid id, CancellationToken ct) =>
        (await sender.Send(new UnlinkRenterZaloCommand(id), ct)).ToHttp();

    [HttpPut("properties/{id:guid}/notification-settings")]
    [EndpointSummary("Bật / tắt tự gửi phiếu khi chốt và cài các mốc nhắc nợ (tối đa 3 mốc, mỗi mốc 1–60 ngày sau hạn, tăng dần; rỗng = không nhắc)")]
    public async Task<IResult> UpdateSettings(Guid id, UpdateNotificationSettingsRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateNotificationSettingsCommand(id, body.NotifyOnFinalize, body.OverdueReminderDays ?? []), ct)).ToHttp();
}

using Mediator;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Billing;
using renting_room.Application.Common.Models;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;

namespace renting_room.Controllers;

/// <summary>Phiếu báo tiền phòng (M07): tạo nháp, sửa, tính lại, chốt, hủy, hoàn tiền.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Invoices")]
[Consumes("application/json")]
public sealed class InvoicesController(ISender sender) : ControllerBase
{
    [HttpPost("invoices/generate")]
    [Idempotent(required: false)]
    [EndpointSummary("Tạo phiếu nháp theo khu + tháng thu (lọc phòng / tầng / 1 hợp đồng)")]
    public async Task<IResult> Generate(GenerateInvoicesRequest b, CancellationToken ct) =>
        (await sender.Send(new GenerateInvoicesCommand(b.PropertyId, b.BillingMonth, b.RoomIds, b.Floor, b.RecalculateExistingDrafts ?? false,
            b.ContractId), ct)).ToHttp();

    [HttpPost("invoices/generate-bulk")]
    [Idempotent(required: false)]
    [EndpointSummary("Tạo phiếu nháp tháng thu cho nhiều khu một lệnh (bỏ trống propertyIds = mọi khu đang dùng) — kết quả từng khu")]
    public async Task<IResult> GenerateBulk(GenerateInvoicesBulkRequest b, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GenerateInvoicesBulkCommand(b.PropertyIds, b.BillingMonth, b.RecalculateExistingDrafts ?? false), ct));

    [HttpGet("invoices/totals")]
    [EndpointSummary("Số tổng hợp theo cùng bộ lọc với danh sách phiếu: phải thu, đã thu, còn nợ, quá hạn; số phiếu nháp và tổng tạm tính")]
    public async Task<IResult> GetTotals(
        Guid? propertyId, string? billingMonth, InvoiceStatus? status, Guid? roomId, Guid? contractId, string? floor,
        bool? unpaidOnly, bool? stale, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetInvoiceTotalsQuery(new ListInvoicesQuery(propertyId, billingMonth, status, roomId, contractId, floor,
            unpaidOnly, Stale: stale)), ct));

    [HttpPost("invoices/recalculate")]
    [EndpointSummary("Tính lại phiếu nháp: theo danh sách phiếu, hoặc khu + tháng (lọc phòng / tầng), hoặc các nháp \"Cần tính lại\" của khu; mặc định giữ ô sửa tay")]
    public async Task<IResult> Recalculate(RecalculateInvoicesRequest b, CancellationToken ct) =>
        (await sender.Send(new RecalculateInvoicesCommand(b.InvoiceIds, b.PropertyId, b.BillingMonth, b.RoomIds, b.Floor,
            b.KeepManualEdits ?? true, b.StaleOnly ?? false), ct)).ToHttp();

    [HttpGet("invoices")]
    [EndpointSummary("Danh sách phiếu (khu, tháng, trạng thái, phòng, hợp đồng, tầng, còn nợ, nháp cần tính lại)")]
    public async Task<IResult> List(
        Guid? propertyId, string? billingMonth, InvoiceStatus? status, Guid? roomId, Guid? contractId, string? floor,
        bool? unpaidOnly, bool? stale, int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListInvoicesQuery(propertyId, billingMonth, status, roomId, contractId, floor, unpaidOnly,
            page ?? 1, pageSize ?? Paging.DefaultPageSize, stale), ct));

    [HttpGet("invoices/{id:guid}")]
    [EndpointSummary("Chi tiết phiếu: dòng, đoạn đo công tơ, vấn đề")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetInvoiceQuery(id), ct)).ToHttp();

    [HttpDelete("invoices/{id:guid}")]
    [EndpointSummary("Xóa phiếu nháp (chỉ phiếu mới nhất của hợp đồng)")]
    public async Task<IResult> DeleteDraft(Guid id, CancellationToken ct) =>
        (await sender.Send(new DeleteDraftInvoiceCommand(id), ct)).ToHttp();

    [HttpPut("invoices/{id:guid}/lines/{lineId:guid}")]
    [EndpointSummary("Sửa tay dòng phiếu nháp (đánh dấu \"sửa tay\", giữ số hệ thống)")]
    public async Task<IResult> EditLine(Guid id, Guid lineId, EditInvoiceLineRequest b, CancellationToken ct) =>
        (await sender.Send(new EditInvoiceLineCommand(id, lineId, b.Quantity, b.UnitPrice, b.Amount, b.Note), ct)).ToHttp();

    [HttpDelete("invoices/{id:guid}/lines/{lineId:guid}")]
    [EndpointSummary("Dòng hệ thống: bỏ sửa tay; phụ thu / giảm tay: xóa dòng")]
    public async Task<IResult> ResetLine(Guid id, Guid lineId, CancellationToken ct) =>
        (await sender.Send(new ResetInvoiceLineCommand(id, lineId), ct)).ToHttp();

    [HttpPost("invoices/{id:guid}/manual-lines")]
    [EndpointSummary("Thêm phụ thu, giảm trừ hoặc hoàn trả (lý do bắt buộc) vào phiếu nháp")]
    public async Task<IResult> AddManualLine(Guid id, AddInvoiceLineRequest b, [FromServices] TimeProvider clock, CancellationToken ct) =>
        (await sender.Send(new AddInvoiceManualLineCommand(id, b.Type, b.Description, b.Quantity, b.UnitPrice, b.Amount, b.Note, b.FeeTypeId,
            b.SourceInvoiceId, Settlement(b.Settled, b.SettledOn, b.Method, clock)), ct)).ToHttp();

    [HttpPost("invoices/manual-lines")]
    [Idempotent(required: false)]
    [EndpointSummary("Thêm cùng một phụ thu / giảm trừ / hoàn trả cho nhiều phòng: theo danh sách phiếu, hoặc khu + tháng (lọc phòng / tầng) — kết quả từng phiếu")]
    public async Task<IResult> BulkAddManualLine(BulkAddInvoiceLineRequest b, [FromServices] TimeProvider clock, CancellationToken ct) =>
        (await sender.Send(new BulkAddManualLineCommand(b.InvoiceIds, b.PropertyId, b.BillingMonth, b.RoomIds, b.Floor,
            b.Type, b.Description, b.Quantity, b.UnitPrice, b.Amount, b.Note, b.FeeTypeId,
            Settlement(b.Settled, b.SettledOn, b.Method, clock)), ct)).ToHttp();

    [HttpPut("invoices/{id:guid}/note")]
    [EndpointSummary("Ghi chú in trên phiếu (nháp)")]
    public async Task<IResult> UpdateNote(Guid id, InvoiceNoteRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateInvoiceNoteCommand(id, b.Note), ct)).ToHttp();

    [HttpPost("invoices/{id:guid}/finalize")]
    [Idempotent(required: false)]
    [EndpointSummary("Chốt phiếu: cấp số PB, hạn thanh toán; dữ liệu nguồn đổi ⇒ 409 DRAFT_STALE")]
    public async Task<IResult> Finalize(Guid id, CancellationToken ct) =>
        (await sender.Send(new FinalizeInvoiceCommand(id), ct)).ToHttp();

    [HttpPost("invoices/finalize-batch")]
    [Idempotent(required: false)]
    [EndpointSummary("Chốt hàng loạt — kết quả từng phiếu")]
    public async Task<IResult> FinalizeBatch(FinalizeBatchRequest b, CancellationToken ct) =>
        Results.Ok(await sender.Send(new FinalizeInvoicesBatchCommand(b.InvoiceIds), ct));

    [HttpPost("invoices/{id:guid}/void")]
    [EndpointSummary("Hủy phiếu đã chốt (chưa thu tiền, phiếu mới nhất của hợp đồng)")]
    public async Task<IResult> Void(Guid id, VoidInvoiceRequest b, CancellationToken ct) =>
        (await sender.Send(new VoidInvoiceCommand(id, b.Reason), ct)).ToHttp();

    [HttpPost("invoices/{id:guid}/refund")]
    [EndpointSummary("Xác nhận đã trả lại người thuê phần tổng âm của phiếu (hoàn trả > phần thu)")]
    public async Task<IResult> ConfirmRefund(Guid id, ConfirmRefundRequest b, CancellationToken ct) =>
        (await sender.Send(new ConfirmInvoiceRefundCommand(id, b.RefundedOn, b.Method, b.Note), ct)).ToHttp();

    [HttpDelete("invoices/{id:guid}/refund")]
    [EndpointSummary("Bỏ xác nhận đã hoàn (nhập nhầm)")]
    public async Task<IResult> CancelRefund(Guid id, CancellationToken ct) =>
        (await sender.Send(new CancelInvoiceRefundCommand(id), ct)).ToHttp();

    [HttpGet("rooms/{roomId:guid}/invoices")]
    [EndpointSummary("Phiếu tiền phòng của phòng (mọi hợp đồng), mới nhất trước")]
    public async Task<IResult> ListByRoom(Guid roomId, int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListInvoicesQuery(null, null, null, roomId, null, null, null, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct));

    /// <summary>"Đã thanh toán / đã hoàn ngay" của dòng tay — ngày bỏ trống = hôm nay, phương thức bỏ trống = tiền mặt.</summary>
    private static ChargeSettlement? Settlement(bool? settled, DateOnly? settledOn, PaymentMethod? method, TimeProvider clock) =>
        settled == true ? new ChargeSettlement(settledOn ?? clock.GetUtcNow().ToBusinessDate(), method ?? PaymentMethod.Cash) : null;
}

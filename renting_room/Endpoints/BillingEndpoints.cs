using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Models;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Idempotency;

namespace renting_room.Endpoints;

public sealed record GenerateInvoicesRequest(Guid PropertyId, string BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool? RecalculateExistingDrafts);

public sealed record RecalculateInvoicesRequest(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor, bool? KeepManualEdits,
    bool? StaleOnly);

public sealed record EditInvoiceLineRequest(decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? Note);

/// <param name="Settled">Phụ thu / giảm trừ đã thanh toán / đã hoàn ngay ⇒ hiện trên phiếu, không tính vào tổng (BL-BR-34).</param>
public sealed record AddInvoiceLineRequest(
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId, Guid? SourceInvoiceId,
    bool? Settled = null, DateOnly? SettledOn = null, PaymentMethod? Method = null);

public sealed record BulkAddInvoiceLineRequest(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    bool? Settled = null, DateOnly? SettledOn = null, PaymentMethod? Method = null);

public sealed record ConfirmRefundRequest(DateOnly RefundedOn, PaymentMethod Method, string? Note);

public sealed record InvoiceNoteRequest(string? Note);

public sealed record FinalizeBatchRequest(IReadOnlyList<Guid> InvoiceIds);

public sealed record VoidInvoiceRequest(string Reason);

public sealed record SaveMeterReadingsRequest(IReadOnlyList<PeriodicReadingInput> Readings);

public sealed record RecordPaymentRequest(
    decimal Amount, PaymentMethod Method, DateOnly PaidAt, Guid? InvoiceId, string? PayerName, string? Reference, string? Note);

public sealed record ReversePaymentRequest(string Reason);

public sealed record WriteOffInvoiceRequest(decimal? Amount, string Reason);

public sealed record PayAndWriteOffRequest(decimal Amount, PaymentMethod Method, DateOnly PaidAt, string? PayerName, string? Reference, string Reason);

/// <summary>Phiếu tiền phòng (M07 đợt 1), lưới ghi chỉ số (M06 đợt 2), thu tiền (M08 đợt 1).</summary>
public static class BillingEndpoints
{
    private const string InvoicesRoute = $"{EndpointHelpers.ApiPrefix}/invoices";
    private const string PaymentsRoute = $"{EndpointHelpers.ApiPrefix}/payments";

    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        MapReadingSheet(app);
        MapInvoices(app);
        MapPayments(app);
    }

    private static void MapReadingSheet(IEndpointRouteBuilder app)
    {
        var properties = app.MapGroup($"{EndpointHelpers.ApiPrefix}/properties/{{propertyId:guid}}").WithTags("Meters");
        properties.MapGet("/meter-reading-sheet", async (Guid propertyId, string billingMonth, string? floor, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetMeterReadingSheetQuery(propertyId, billingMonth, floor), ct)).ToHttp())
            .WithSummary("Lưới ghi chỉ số cuối kỳ của khu theo tháng thu");
        properties.MapPut("/meter-readings", async (Guid propertyId, SaveMeterReadingsRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SaveMeterReadingsCommand(propertyId, b.Readings), ct)).ToHttp())
            .WithIdempotency(required: false)
            .WithSummary("Lưu chỉ số cuối kỳ hàng loạt (lỗi 1 dòng ⇒ không lưu dòng nào)");
    }

    private static void MapInvoices(IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup(InvoicesRoute).WithTags("Invoices");
        invoices.MapPost("/generate", async (GenerateInvoicesRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GenerateInvoicesCommand(b.PropertyId, b.BillingMonth, b.RoomIds, b.Floor, b.RecalculateExistingDrafts ?? false), ct)).ToHttp())
            .WithIdempotency(required: false)
            .WithSummary("Tạo phiếu nháp theo khu + tháng thu (lọc phòng / tầng)");
        invoices.MapPost("/recalculate", async (RecalculateInvoicesRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RecalculateInvoicesCommand(b.InvoiceIds, b.PropertyId, b.BillingMonth, b.RoomIds, b.Floor,
                    b.KeepManualEdits ?? true, b.StaleOnly ?? false), ct)).ToHttp())
            .WithSummary("Tính lại phiếu nháp: theo danh sách phiếu, hoặc khu + tháng (lọc phòng / tầng), hoặc các nháp \"Cần tính lại\" của khu; mặc định giữ ô sửa tay");
        invoices.MapGet("/", async (Guid? propertyId, string? billingMonth, InvoiceStatus? status, Guid? roomId, Guid? contractId, string? floor,
                    bool? unpaidOnly, bool? stale, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListInvoicesQuery(propertyId, billingMonth, status, roomId, contractId, floor, unpaidOnly,
                    page ?? 1, pageSize ?? Paging.DefaultPageSize, stale), ct)))
            .WithSummary("Danh sách phiếu (khu, tháng, trạng thái, phòng, hợp đồng, tầng, còn nợ, nháp cần tính lại)");
        invoices.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetInvoiceQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết phiếu: dòng, đoạn đo công tơ, vấn đề");
        invoices.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new DeleteDraftInvoiceCommand(id), ct)).ToHttp())
            .WithSummary("Xóa phiếu nháp (chỉ phiếu mới nhất của hợp đồng)");
        invoices.MapPut("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, EditInvoiceLineRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new EditInvoiceLineCommand(id, lineId, b.Quantity, b.UnitPrice, b.Amount, b.Note), ct)).ToHttp())
            .WithSummary("Sửa tay dòng phiếu nháp (đánh dấu \"sửa tay\", giữ số hệ thống)");
        invoices.MapDelete("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ResetInvoiceLineCommand(id, lineId), ct)).ToHttp())
            .WithSummary("Dòng hệ thống: bỏ sửa tay; phụ thu / giảm tay: xóa dòng");
        invoices.MapPost("/{id:guid}/manual-lines", async (Guid id, AddInvoiceLineRequest b, ISender sender, TimeProvider clock, CancellationToken ct) =>
                (await sender.Send(new AddInvoiceManualLineCommand(id, b.Type, b.Description, b.Quantity, b.UnitPrice, b.Amount, b.Note, b.FeeTypeId,
                    b.SourceInvoiceId, Settlement(b.Settled, b.SettledOn, b.Method, clock)), ct)).ToHttp())
            .WithSummary("Thêm phụ thu, giảm trừ hoặc hoàn trả (lý do bắt buộc) vào phiếu nháp");
        invoices.MapPost("/manual-lines", async (BulkAddInvoiceLineRequest b, ISender sender, TimeProvider clock, CancellationToken ct) =>
                (await sender.Send(new BulkAddManualLineCommand(b.InvoiceIds, b.PropertyId, b.BillingMonth, b.RoomIds, b.Floor,
                    b.Type, b.Description, b.Quantity, b.UnitPrice, b.Amount, b.Note, b.FeeTypeId,
                    Settlement(b.Settled, b.SettledOn, b.Method, clock)), ct)).ToHttp())
            .WithIdempotency(required: false)
            .WithSummary("Thêm cùng một phụ thu / giảm trừ / hoàn trả cho nhiều phòng: theo danh sách phiếu, hoặc khu + tháng (lọc phòng / tầng) — kết quả từng phiếu");
        invoices.MapPut("/{id:guid}/note", async (Guid id, InvoiceNoteRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateInvoiceNoteCommand(id, b.Note), ct)).ToHttp())
            .WithSummary("Ghi chú in trên phiếu (nháp)");
        invoices.MapPost("/{id:guid}/finalize", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new FinalizeInvoiceCommand(id), ct)).ToHttp())
            .WithIdempotency(required: false)
            .WithSummary("Chốt phiếu: cấp số PB, hạn thanh toán; dữ liệu nguồn đổi ⇒ 409 DRAFT_STALE");
        invoices.MapPost("/finalize-batch", async (FinalizeBatchRequest b, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new FinalizeInvoicesBatchCommand(b.InvoiceIds), ct)))
            .WithIdempotency(required: false)
            .WithSummary("Chốt hàng loạt — kết quả từng phiếu");
        invoices.MapPost("/{id:guid}/void", async (Guid id, VoidInvoiceRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new VoidInvoiceCommand(id, b.Reason), ct)).ToHttp())
            .WithSummary("Hủy phiếu đã chốt (chưa thu tiền, phiếu mới nhất của hợp đồng)");
        invoices.MapPost("/{id:guid}/refund", async (Guid id, ConfirmRefundRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ConfirmInvoiceRefundCommand(id, b.RefundedOn, b.Method, b.Note), ct)).ToHttp())
            .WithSummary("Xác nhận đã trả lại người thuê phần tổng âm của phiếu (hoàn trả > phần thu)");
        invoices.MapDelete("/{id:guid}/refund", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CancelInvoiceRefundCommand(id), ct)).ToHttp())
            .WithSummary("Bỏ xác nhận đã hoàn (nhập nhầm)");

        app.MapGroup($"{EndpointHelpers.ApiPrefix}/rooms/{{roomId:guid}}").WithTags("Invoices")
            .MapGet("/invoices", async (Guid roomId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListInvoicesQuery(null, null, null, roomId, null, null, null, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Phiếu tiền phòng của phòng (mọi hợp đồng), mới nhất trước");
    }

    /// <summary>"Đã thanh toán / đã hoàn ngay" của dòng tay — ngày bỏ trống = hôm nay, phương thức bỏ trống = tiền mặt.</summary>
    private static ChargeSettlement? Settlement(bool? settled, DateOnly? settledOn, PaymentMethod? method, TimeProvider clock) =>
        settled == true ? new ChargeSettlement(settledOn ?? clock.GetUtcNow().ToBusinessDate(), method ?? PaymentMethod.Cash) : null;

    private static void MapPayments(IEndpointRouteBuilder app)
    {
        app.MapGroup($"{EndpointHelpers.ApiPrefix}/contracts/{{contractId:guid}}").WithTags("Payments")
            .MapPost("/payments", async (Guid contractId, RecordPaymentRequest b, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new RecordPaymentCommand(contractId, b.Amount, b.Method, b.PaidAt, b.InvoiceId, b.PayerName, b.Reference, b.Note), ct);
                return result.IsSuccess ? Results.Created($"{PaymentsRoute}/{result.Value!.Id}", result.Value) : result.ToHttp();
            })
            .WithIdempotency(required: true)
            .WithSummary("Ghi thu (\"Đã thu\"): chỉ định phiếu hoặc tự động phiếu cũ nhất trước — cấp số PT");

        // F2 (PM-BR-16/29): bỏ nợ bất kỳ lúc nào — chủ trọ hoặc phó quản lý được cấp quyền (kiểm trong handler).
        var writeOffs = app.MapGroup(InvoicesRoute).WithTags("Payments");
        writeOffs.MapPost("/{id:guid}/write-off", async (Guid id, WriteOffInvoiceRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new WriteOffInvoiceCommand(id, b.Amount, b.Reason), ct)).ToHttp())
            .WithIdempotency(required: true)
            .WithSummary("Bỏ nợ phiếu (mặc định toàn bộ số còn nợ) — chủ trọ / phó quản lý được cấp quyền, lý do bắt buộc, không tính doanh thu");
        writeOffs.MapPost("/{id:guid}/pay-and-write-off", async (Guid id, PayAndWriteOffRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new PayAndWriteOffCommand(id, b.Amount, b.Method, b.PaidAt, b.PayerName, b.Reference, b.Reason), ct)).ToHttp())
            .WithIdempotency(required: true)
            .WithSummary("Thanh toán + bỏ phần còn lại của phiếu (VD nợ 3tr, trả 2tr, cho 1tr) — chủ trọ / phó quản lý được cấp quyền");

        var payments = app.MapGroup(PaymentsRoute).WithTags("Payments");
        payments.MapGet("/", async (Guid? contractId, Guid? roomId, Guid? invoiceId, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListPaymentsQuery(contractId, roomId, invoiceId), ct)))
            .WithSummary("Phiếu thu theo hợp đồng / phòng / phiếu báo");
        payments.MapPost("/{id:guid}/reverse", async (Guid id, ReversePaymentRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ReversePaymentCommand(id, b.Reason), ct)).ToHttp())
            .WithSummary("Đảo phiếu thu nhập sai (hủy phân bổ, phiếu báo về còn nợ)");
    }
}

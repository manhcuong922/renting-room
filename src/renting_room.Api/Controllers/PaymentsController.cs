using Mediator;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Payments;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Payments")]
[Consumes("application/json")]
public sealed class PaymentsController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/payments";

    [HttpGet("contracts/{contractId:guid}/balance")]
    [EndpointSummary("Số nợ của hợp đồng: tổng còn nợ, phần quá hạn, tiền chờ hoàn, từng phiếu còn nợ — cho màn thu theo hợp đồng")]
    public async Task<IResult> GetContractBalance(Guid contractId, CancellationToken ct) =>
        (await sender.Send(new GetContractBalanceQuery(contractId), ct)).ToHttp();

    [HttpPost("contracts/{contractId:guid}/payments")]
    [Idempotent]
    [EndpointSummary("Ghi thu (\"Đã thu\"): chỉ định phiếu hoặc tự động phiếu cũ nhất trước — cấp số PT")]
    public async Task<IResult> Record(Guid contractId, RecordPaymentRequest b, CancellationToken ct)
    {
        var result = await sender.Send(new RecordPaymentCommand(contractId, b.Amount, b.Method, b.PaidAt, b.InvoiceId, b.PayerName, b.Reference, b.Note), ct);
        return result.IsSuccess ? Results.Created($"{Route}/{result.Value!.Id}", result.Value) : result.ToHttp();
    }

    [HttpPost("invoices/{id:guid}/write-off")]
    [Idempotent]
    [EndpointSummary("Bỏ nợ phiếu (mặc định toàn bộ số còn nợ) — chủ trọ / phó quản lý được cấp quyền, lý do bắt buộc, không tính doanh thu")]
    public async Task<IResult> WriteOff(Guid id, WriteOffInvoiceRequest b, CancellationToken ct) =>
        (await sender.Send(new WriteOffInvoiceCommand(id, b.Amount, b.Reason), ct)).ToHttp();

    [HttpPost("invoices/{id:guid}/pay-and-write-off")]
    [Idempotent]
    [EndpointSummary("Thanh toán + bỏ phần còn lại của phiếu (VD nợ 3tr, trả 2tr, cho 1tr) — chủ trọ / phó quản lý được cấp quyền")]
    public async Task<IResult> PayAndWriteOff(Guid id, PayAndWriteOffRequest b, CancellationToken ct) =>
        (await sender.Send(new PayAndWriteOffCommand(id, b.Amount, b.Method, b.PaidAt, b.PayerName, b.Reference, b.Reason), ct)).ToHttp();

    [HttpGet("payments")]
    [EndpointSummary("Phiếu thu theo hợp đồng / phòng / phiếu báo")]
    public async Task<IResult> List(Guid? contractId, Guid? roomId, Guid? invoiceId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListPaymentsQuery(contractId, roomId, invoiceId), ct));

    [HttpPost("payments/{id:guid}/reverse")]
    [EndpointSummary("Đảo phiếu thu nhập sai (hủy phân bổ, phiếu báo về còn nợ)")]
    public async Task<IResult> Reverse(Guid id, ReversePaymentRequest b, CancellationToken ct) =>
        (await sender.Send(new ReversePaymentCommand(id, b.Reason), ct)).ToHttp();
}

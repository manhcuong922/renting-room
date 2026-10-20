using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using renting_room.Application.Billing;
using renting_room.Application.Common.Models;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Contracts")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class ContractsController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/contracts";
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [HttpGet("contracts")]
    [EndpointSummary("Danh sách hợp đồng — lọc khu, phòng, người thuê, trạng thái, sắp hết hạn, quá hạn, có/không cọc, chưa hoàn cọc, thiếu bản HĐ ký")]
    public async Task<IResult> List(
        Guid? propertyId, Guid? roomId, Guid? renterId, ContractStatus? status, int? expiringWithinDays, bool? overdue,
        string? search, bool? hasDeposit, bool? missingSignedDocument, bool? depositNotRefunded, int? page, int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListContractsQuery(
            propertyId, roomId, renterId, status, expiringWithinDays, overdue, search, page ?? 1, pageSize ?? Paging.DefaultPageSize,
            hasDeposit, missingSignedDocument, depositNotRefunded), ct));

    [HttpPost("contracts")]
    [Idempotent]
    [EndpointSummary("Tạo hợp đồng nháp (trường bỏ trống lấy mặc định từ khu/phòng)")]
    public async Task<IResult> Create(CreateContractRequest body, CancellationToken ct) =>
        (await sender.Send(new CreateContractCommand(body.RoomId, body.ContractNo, body.Contract), ct)).ToCreated(Route);

    [HttpGet("contracts/{id:guid}")]
    [EndpointSummary("Chi tiết hợp đồng: giá theo thời gian, người ở, tài sản, xe, bên cho thuê (snapshot)")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetContractQuery(id), ct)).ToHttp();

    [HttpPut("contracts/{id:guid}")]
    [EndpointSummary("Sửa hợp đồng nháp")]
    public async Task<IResult> UpdateDraft(Guid id, UpdateContractRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateContractDraftCommand(id, body.Contract, body.Version), ct)).ToHttp();

    [HttpPut("contracts/{id:guid}/note")]
    [EndpointSummary("Sửa ghi chú nội bộ (mọi trạng thái trừ đã hủy) — nội dung đã ký đổi qua phụ lục")]
    public async Task<IResult> UpdateNote(Guid id, UpdateContractNoteRequest body, CancellationToken ct) =>
        (await sender.Send(new UpdateContractNoteCommand(id, body.Note), ct)).ToHttp();

    [HttpPut("contracts/{id:guid}/signed-document")]
    [EndpointSummary("Đánh dấu đã có / chưa có bản HĐ ký (giấy, ảnh, PDF) + nơi cất — hết cờ \"Thiếu tài liệu\"")]
    public async Task<IResult> SetSignedDocument(Guid id, SignedDocumentRequest body, CancellationToken ct) =>
        (await sender.Send(new SetSignedDocumentCommand(id, body.HasSignedDocument, body.Note), ct)).ToHttp();

    [HttpPut("contracts/{id:guid}/fees/{feeTypeId:guid}")]
    [EndpointSummary("HĐ đang hiệu lực: gắn thêm / đổi số lượng, giá riêng — effectiveFrom bỏ trống ⇒ từ kỳ chưa chốt đầu tiên")]
    public async Task<IResult> ChangeFee(Guid id, Guid feeTypeId, ChangeContractFeeRequest b, CancellationToken ct) =>
        (await sender.Send(new ChangeContractFeeCommand(id, feeTypeId, b.Quantity, b.UnitPriceOverride, b.EffectiveFrom), ct)).ToHttp();

    [HttpDelete("contracts/{id:guid}/fees/{feeTypeId:guid}")]
    [EndpointSummary("HĐ đang hiệu lực: thôi tính khoản thu từ đầu kỳ ?effectiveFrom=")]
    public async Task<IResult> RemoveFee(Guid id, Guid feeTypeId, DateOnly? effectiveFrom, CancellationToken ct) =>
        (await sender.Send(new RemoveContractFeeCommand(id, feeTypeId, effectiveFrom), ct)).ToHttp();

    [HttpGet("contracts/data-review")]
    [EndpointSummary("Rà HĐ đang hiệu lực tìm dữ liệu cần xem lại (quan hệ người ở, tuổi người ký, ở 2 phòng, khoản thu chưa có giá)")]
    public async Task<IResult> GetDataReview(Guid? propertyId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetContractDataReviewQuery(propertyId), ct));

    [HttpGet("contracts/{id:guid}/document")]
    [NoStore]
    [EndpointSummary("Tải văn bản hợp đồng (.docx) để in / ký — nháp có dòng \"BẢN NHÁP\"; có số giấy tờ đầy đủ (ghi log)")]
    public async Task<IResult> GetDocument(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetContractDocumentQuery(id), ct);
        return result.IsSuccess
            ? Results.File(result.Value!.Content, DocxContentType, result.Value.FileName)
            : result.ToHttp();
    }

    [HttpPost("contracts/{id:guid}/cancel")]
    [EndpointSummary("Hủy hợp đồng nháp")]
    public async Task<IResult> Cancel(Guid id, CancelContractRequest body, CancellationToken ct) =>
        (await sender.Send(new CancelContractCommand(id, body.Reason), ct)).ToWarnings();

    [HttpPost("contracts/{id:guid}/activate")]
    [Idempotent(required: false)]
    [EndpointSummary("Kích hoạt (bàn giao phòng): kiểm tra bên cho thuê, người ký ≥ 18 tuổi, sức chứa; chụp snapshot")]
    public async Task<IResult> Activate(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ActivateContractRequest? body, CancellationToken ct) =>
        (await sender.Send(new ActivateContractCommand(id, body?.HandoverReadings), ct)).ToHttp();

    [HttpGet("contracts/{id:guid}/billing-periods")]
    [EndpointSummary("Lịch kỳ thu của hợp đồng")]
    public async Task<IResult> GetBillingPeriods(Guid id, DateOnly? until, CancellationToken ct) =>
        (await sender.Send(new GetBillingPeriodsQuery(id, until), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/occupants")]
    [EndpointSummary("Thêm người ở (vai trò: người ở / đồng thuê)")]
    public async Task<IResult> AddOccupant(Guid id, AddOccupantRequest b, CancellationToken ct) =>
        (await sender.Send(new AddOccupantCommand(id, b.RenterId, b.MoveInDate, b.ExpectedEndDate, b.Relationship, b.Note,
            b.RelationshipType, b.GuardianConsent ?? false, b.Role ?? OccupantRole.Resident), ct)).ToHttp();

    [HttpPut("contracts/{id:guid}/occupants/{occupantId:guid}/role")]
    [EndpointSummary("Đổi vai trò người ở ↔ đồng thuê (đồng thuê chỉ mang tính giấy tờ, phải có giấy tờ)")]
    public async Task<IResult> ChangeOccupantRole(Guid id, Guid occupantId, ChangeOccupantRoleRequest b, CancellationToken ct) =>
        (await sender.Send(new ChangeOccupantRoleCommand(id, occupantId, b.Role), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/occupants/{occupantId:guid}/move")]
    [EndpointSummary("Chuyển người sang hợp đồng đang hiệu lực của phòng khác (xe của người đó chuyển theo)")]
    public async Task<IResult> MoveOccupant(Guid id, Guid occupantId, MoveOccupantRequest b, CancellationToken ct) =>
        (await sender.Send(new MoveOccupantCommand(id, occupantId, b.ToContractId, b.Date, b.Role ?? OccupantRole.Resident, b.RelationshipType,
            b.Relationship, b.GuardianConsent ?? false, b.Note), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/representative")]
    [EndpointSummary("Đổi người đứng tên trên cùng hợp đồng (nợ, cọc giữ nguyên; bản ký về Thiếu tài liệu)")]
    public async Task<IResult> ChangeRepresentative(Guid id, ChangeRepresentativeRequest b, CancellationToken ct) =>
        (await sender.Send(new ChangeRepresentativeCommand(id, b.RenterId, b.Date, b.HouseholdHeadRenterId, b.OldRepresentativeMovesOut ?? false), ct))
        .ToHttp();

    [HttpPost("contracts/{id:guid}/occupants/{occupantId:guid}/end")]
    [EndpointSummary("Ghi nhận người ở rời đi")]
    public async Task<IResult> EndOccupancy(Guid id, Guid occupantId, EndOccupancyRequest b, CancellationToken ct) =>
        (await sender.Send(new EndOccupancyCommand(id, occupantId, b.MoveOutDate), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/rent-terms")]
    [EndpointSummary("Sửa giá thuê — effectiveFrom bỏ trống ⇒ từ kỳ chưa chốt đầu tiên (lịch sử giữ nguyên)")]
    public async Task<IResult> ChangeRent(Guid id, ChangeRentRequest b, CancellationToken ct) =>
        (await sender.Send(new ChangeRentCommand(id, b.EffectiveFrom, b.MonthlyRent, b.AddendumNo, b.Note), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/extend")]
    [EndpointSummary("Gia hạn hợp đồng")]
    public async Task<IResult> Extend(Guid id, ExtendContractRequest b, CancellationToken ct) =>
        (await sender.Send(new ExtendContractCommand(id, b.NewEndDate), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/holdover")]
    [EndpointSummary("HĐ đã hết hạn: cho ở tiếp, chưa ký lại (CT-UC-22)")]
    public async Task<IResult> StartHoldover(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] HoldoverRequest? b, CancellationToken ct) =>
        (await sender.Send(new StartHoldoverCommand(id, b?.Note), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/deposit/refund")]
    [EndpointSummary("Đã hoàn trả cọc (ngày, số tiền thực trả — mặc định đủ cọc, ghi chú bắt buộc nếu trả ít hơn)")]
    public async Task<IResult> RefundDeposit(Guid id, RefundDepositRequest b, CancellationToken ct) =>
        (await sender.Send(new RefundDepositCommand(id, b.RefundedOn, b.Amount, b.Note), ct)).ToHttp();

    [HttpDelete("contracts/{id:guid}/deposit/refund")]
    [EndpointSummary("Bỏ đánh dấu hoàn trả / chuyển cọc (nhập nhầm)")]
    public async Task<IResult> CancelDepositRefund(Guid id, CancellationToken ct) =>
        (await sender.Send(new CancelDepositRefundCommand(id), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/transfer-room")]
    [Idempotent]
    [EndpointSummary("Chuyển phòng (cùng khu) từ hôm nay: HĐ giữ nguyên, chỉ số cuối phòng cũ + nhận phòng mới (sản lượng phòng cũ cộng vào dòng điện nước của kỳ); giá mới theo quy tắc nửa kỳ, bỏ dịch vụ không dùng ở phòng mới. Về đúng phòng vừa rời trong ngày chuyển = xóa lần chuyển")]
    public async Task<IResult> TransferRoom(Guid id, TransferRoomRequest b, CancellationToken ct)
    {
        var result = await sender.Send(new TransferRoomCommand(id, b.ToRoomId, b.OldRoomReadings, b.NewRoomReadings, b.MonthlyRent, b.Note, b.RentFrom,
            b.RemoveFeeTypeIds), ct);
        return result.ToWarnings();
    }

    [HttpPost("contracts/{id:guid}/re-sign")]
    [Idempotent]
    [EndpointSummary("Ký lại cho người còn ở: thanh lý HĐ cũ tại ngày bàn giao, tạo HĐ nháp mới từ ngày hôm sau (CT-UC-21)")]
    public async Task<IResult> Resign(Guid id, ResignContractRequest b, CancellationToken ct) =>
        (await sender.Send(new ResignContractCommand(id, b.HandoverDate, b.RepresentativeRenterId, b.EndDate, b.TransferDeposit ?? true), ct)).ToCreated(Route);

    [HttpPost("contracts/{id:guid}/notice")]
    [EndpointSummary("Báo trả phòng — trả cảnh báo nếu báo trước ít hơn thỏa thuận (thường 30 ngày)")]
    public async Task<IResult> GiveNotice(Guid id, GiveNoticeRequest b, CancellationToken ct) =>
        (await sender.Send(new GiveNoticeCommand(id, b.NoticeDate, b.PlannedMoveOutDate), ct)).ToHttp();

    [HttpGet("contracts/{id:guid}/liquidation/preview")]
    [EndpointSummary("Xem trước thanh lý: phiếu còn nợ, tiền chờ hoàn, chỉ số cuối còn thiếu, hóa đơn trả phòng, cọc, những gì còn chặn hoàn tất")]
    public async Task<IResult> PreviewLiquidation(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetLiquidationPreviewQuery(id), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/liquidation/start")]
    [EndpointSummary("Bắt đầu thanh lý (bên cho thuê đơn phương ⇒ phải nêu căn cứ Điều 172)")]
    public async Task<IResult> StartLiquidation(Guid id, StartLiquidationRequest b, CancellationToken ct) =>
        (await sender.Send(new StartLiquidationCommand(id, b.ActualEndDate, b.Reason, b.Ground, b.Note), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/liquidation/cancel")]
    [EndpointSummary("Hủy thanh lý, quay lại đang hiệu lực")]
    public async Task<IResult> CancelLiquidation(Guid id, CancellationToken ct) =>
        (await sender.Send(new CancelLiquidationCommand(id), ct)).ToWarnings();

    [HttpPost("contracts/{id:guid}/liquidation/complete")]
    [EndpointSummary("Hoàn tất thanh lý: cần hóa đơn trả phòng đã chốt; còn nợ ⇒ settlement CollectAll (đã thu toàn bộ) / WriteOff (bỏ nợ)")]
    public async Task<IResult> CompleteLiquidation(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteLiquidationRequest? body, CancellationToken ct) =>
        (await sender.Send(new CompleteLiquidationCommand(id, body?.Settlement, body?.Method, body?.PaidAt, body?.Reason), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/final-invoice")]
    [Idempotent(required: false)]
    [EndpointSummary("Lập hóa đơn trả phòng khi trả phòng (nhập chỉ số cuối) — nháp sửa được như phiếu thường")]
    public async Task<IResult> CreateFinalInvoice(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FinalInvoiceRequest? body, CancellationToken ct)
    {
        var result = await sender.Send(new CreateFinalInvoiceCommand(id, body?.FinalReadings), ct);
        return result.IsSuccess ? Results.Created($"/api/v1/invoices/{result.Value!.Summary.Id}", result.Value) : result.ToHttp();
    }

    [HttpPost("contracts/{id:guid}/assets")]
    [EndpointSummary("Thêm tài sản bàn giao (khi hợp đồng còn nháp)")]
    public async Task<IResult> AddAsset(Guid id, AssetRequest b, CancellationToken ct) =>
        (await sender.Send(new AddAssetCommand(id, b), ct)).ToCreated($"{Route}/{id}/assets");

    [HttpPut("contracts/{id:guid}/assets/{assetId:guid}")]
    [EndpointSummary("Sửa tài sản bàn giao")]
    public async Task<IResult> UpdateAsset(Guid id, Guid assetId, AssetRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateAssetCommand(id, assetId, b), ct)).ToHttp();

    [HttpDelete("contracts/{id:guid}/assets/{assetId:guid}")]
    [EndpointSummary("Xóa tài sản bàn giao")]
    public async Task<IResult> RemoveAsset(Guid id, Guid assetId, CancellationToken ct) =>
        (await sender.Send(new RemoveAssetCommand(id, assetId), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/assets/{assetId:guid}/return")]
    [EndpointSummary("Ghi tình trạng tài sản khi trả phòng (bồi thường: thêm phụ thu trên hóa đơn trả phòng)")]
    public async Task<IResult> RecordAssetReturn(Guid id, Guid assetId, AssetReturnRequest b, CancellationToken ct) =>
        (await sender.Send(new RecordAssetReturnCommand(id, assetId, b.ConditionAtReturn), ct)).ToHttp();

    [HttpPost("contracts/{id:guid}/vehicles")]
    [EndpointSummary("Đăng ký xe gửi (1 biển số chỉ đăng ký ở 1 nơi)")]
    public async Task<IResult> RegisterVehicle(Guid id, RegisterVehicleRequest b, CancellationToken ct) =>
        (await sender.Send(new RegisterVehicleCommand(id, b.RenterId, b.VehicleType, b.PlateNumber, b.BrandColor, b.RegisteredFrom, b.Note), ct))
        .ToCreated($"{Route}/{id}/vehicles");

    [HttpPost("contracts/{id:guid}/vehicles/{vehicleId:guid}/end")]
    [EndpointSummary("Kết thúc đăng ký xe")]
    public async Task<IResult> EndVehicle(Guid id, Guid vehicleId, EndVehicleRequest b, CancellationToken ct) =>
        (await sender.Send(new EndVehicleCommand(id, vehicleId, b.EndDate), ct)).ToHttp();
}

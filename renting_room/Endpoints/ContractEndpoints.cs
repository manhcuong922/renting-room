using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Models;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Domain.Contracts;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record CreateContractRequest(Guid RoomId, string? ContractNo, ContractInput Contract);

public sealed record UpdateContractRequest(ContractInput Contract, uint Version);

public sealed record CancelContractRequest(string Reason);

public sealed record UpdateContractNoteRequest(string? Note);

public sealed record SignedDocumentRequest(bool HasSignedDocument, string? Note);

public sealed record ChangeContractFeeRequest(decimal? Quantity, decimal? UnitPriceOverride, DateOnly? EffectiveFrom);

public sealed record AddOccupantRequest(
    Guid RenterId, DateOnly MoveInDate, DateOnly? ExpectedEndDate, string? Relationship, string? Note,
    OccupantRelationship? RelationshipType = null, bool? GuardianConsent = null);

public sealed record EndOccupancyRequest(DateOnly MoveOutDate);

/// <summary>Số người ở không bị giới hạn (PR-BR-06) — client cũ gửi <c>overrideCapacity</c> thì bị bỏ qua.</summary>
public sealed record ActivateContractRequest(IReadOnlyList<MeterReadingInput>? HandoverReadings);

public sealed record CompleteLiquidationRequest(DebtSettlement? Settlement, PaymentMethod? Method, DateOnly? PaidAt, string? Reason);

public sealed record FinalInvoiceRequest(IReadOnlyList<MeterReadingInput>? FinalReadings);

public sealed record ChangeRentRequest(DateOnly? EffectiveFrom, decimal MonthlyRent, string? AddendumNo, string? Note);

public sealed record ExtendContractRequest(DateOnly NewEndDate);

public sealed record HoldoverRequest(string? Note);

public sealed record ResignContractRequest(DateOnly HandoverDate, Guid RepresentativeRenterId, DateOnly? EndDate);

public sealed record GiveNoticeRequest(DateOnly NoticeDate, DateOnly PlannedMoveOutDate);

public sealed record StartLiquidationRequest(DateOnly ActualEndDate, TerminationReason Reason, TerminationGround? Ground, string? Note);

public sealed record AssetReturnRequest(string? ConditionAtReturn, decimal? CompensationValue);

public sealed record RegisterVehicleRequest(Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly? RegisteredFrom, string? Note);

public sealed record EndVehicleRequest(DateOnly EndDate);

/// <summary>Hợp đồng (M05 — phần cơ bản). Vòng đời: Draft → Active → Liquidating → Ended; Draft → Cancelled.</summary>
public static class ContractEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/contracts";
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static void MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithTags("Contracts").RequireAuthorization(AuthPolicies.OrgMember);

        group.MapGet("/", async (
                Guid? propertyId, Guid? roomId, Guid? renterId, ContractStatus? status, int? expiringWithinDays, bool? overdue,
                string? search, bool? hasDeposit, bool? missingSignedDocument, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListContractsQuery(
                    propertyId, roomId, renterId, status, expiringWithinDays, overdue, search, page ?? 1, pageSize ?? Paging.DefaultPageSize,
                    hasDeposit, missingSignedDocument), ct)))
            .WithSummary("Danh sách hợp đồng — lọc khu, phòng, người thuê, trạng thái, sắp hết hạn, quá hạn, có/không cọc, thiếu bản HĐ ký");

        group.MapPost("/", async (CreateContractRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateContractCommand(body.RoomId, body.ContractNo, body.Contract), ct)).ToCreated(Route))
            .WithIdempotency(required: true)
            .WithSummary("Tạo hợp đồng nháp (trường bỏ trống lấy mặc định từ khu/phòng)");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetContractQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết hợp đồng: giá theo thời gian, người ở, tài sản, xe, bên cho thuê (snapshot)");

        group.MapPut("/{id:guid}", async (Guid id, UpdateContractRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateContractDraftCommand(id, body.Contract, body.Version), ct)).ToHttp())
            .WithSummary("Sửa hợp đồng nháp");

        group.MapPut("/{id:guid}/note", async (Guid id, UpdateContractNoteRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateContractNoteCommand(id, body.Note), ct)).ToHttp())
            .WithSummary("Sửa ghi chú nội bộ (mọi trạng thái trừ đã hủy) — nội dung đã ký đổi qua phụ lục");
        group.MapPut("/{id:guid}/signed-document", async (Guid id, SignedDocumentRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetSignedDocumentCommand(id, body.HasSignedDocument, body.Note), ct)).ToHttp())
            .WithSummary("Đánh dấu đã có / chưa có bản HĐ ký (giấy, ảnh, PDF) + nơi cất — hết cờ \"Thiếu tài liệu\"");

        group.MapPut("/{id:guid}/fees/{feeTypeId:guid}", async (Guid id, Guid feeTypeId, ChangeContractFeeRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeContractFeeCommand(id, feeTypeId, b.Quantity, b.UnitPriceOverride, b.EffectiveFrom), ct)).ToHttp())
            .WithSummary("HĐ đang hiệu lực: gắn thêm / đổi số lượng, giá riêng — effectiveFrom bỏ trống ⇒ từ kỳ chưa chốt đầu tiên");
        group.MapDelete("/{id:guid}/fees/{feeTypeId:guid}", async (Guid id, Guid feeTypeId, DateOnly? effectiveFrom, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemoveContractFeeCommand(id, feeTypeId, effectiveFrom), ct)).ToHttp())
            .WithSummary("HĐ đang hiệu lực: thôi tính khoản thu từ đầu kỳ ?effectiveFrom=");

        group.MapGet("/data-review", async (Guid? propertyId, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetContractDataReviewQuery(propertyId), ct)))
            .WithSummary("Rà HĐ đang hiệu lực tìm dữ liệu cần xem lại (quan hệ người ở, tuổi người ký, ở 2 phòng, khoản thu chưa có giá)");

        group.MapGet("/{id:guid}/document", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetContractDocumentQuery(id), ct);
                return result.IsSuccess
                    ? Results.File(result.Value!.Content, DocxContentType, result.Value.FileName)
                    : result.ToHttp();
            })
            .WithNoStore()
            .Produces(StatusCodes.Status200OK, contentType: DocxContentType)
            .WithSummary("Tải văn bản hợp đồng (.docx) để in / ký — nháp có dòng \"BẢN NHÁP\"; có số giấy tờ đầy đủ (ghi log)");

        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelContractRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CancelContractCommand(id, body.Reason), ct)).ToHttp())
            .WithSummary("Hủy hợp đồng nháp");

        group.MapPost("/{id:guid}/activate", async (Guid id, ActivateContractRequest? body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ActivateContractCommand(id, body?.HandoverReadings), ct)).ToHttp())
            .WithIdempotency(required: false)
            .WithSummary("Kích hoạt (bàn giao phòng): kiểm tra bên cho thuê, người ký ≥ 18 tuổi, sức chứa; chụp snapshot");

        group.MapGet("/{id:guid}/billing-periods", async (Guid id, DateOnly? until, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetBillingPeriodsQuery(id, until), ct)).ToHttp())
            .WithSummary("Lịch kỳ thu của hợp đồng");

        // ---- Người ở
        group.MapPost("/{id:guid}/occupants", async (Guid id, AddOccupantRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AddOccupantCommand(id, b.RenterId, b.MoveInDate, b.ExpectedEndDate, b.Relationship, b.Note,
                    b.RelationshipType, b.GuardianConsent ?? false), ct)).ToHttp())
            .WithSummary("Thêm người ở");
        group.MapPost("/{id:guid}/occupants/{occupantId:guid}/end", async (Guid id, Guid occupantId, EndOccupancyRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new EndOccupancyCommand(id, occupantId, b.MoveOutDate), ct)).ToHttp())
            .WithSummary("Ghi nhận người ở rời đi");

        // ---- Phụ lục
        group.MapPost("/{id:guid}/rent-terms", async (Guid id, ChangeRentRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeRentCommand(id, b.EffectiveFrom, b.MonthlyRent, b.AddendumNo, b.Note), ct)).ToHttp())
            .WithSummary("Sửa giá thuê — effectiveFrom bỏ trống ⇒ từ kỳ chưa chốt đầu tiên (lịch sử giữ nguyên)");
        group.MapPost("/{id:guid}/extend", async (Guid id, ExtendContractRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ExtendContractCommand(id, b.NewEndDate), ct)).ToHttp())
            .WithSummary("Gia hạn hợp đồng");
        group.MapPost("/{id:guid}/holdover", async (Guid id, HoldoverRequest? b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new StartHoldoverCommand(id, b?.Note), ct)).ToHttp())
            .WithSummary("HĐ đã hết hạn: cho ở tiếp, chưa ký lại (CT-UC-22)");
        group.MapPost("/{id:guid}/re-sign", async (Guid id, ResignContractRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ResignContractCommand(id, b.HandoverDate, b.RepresentativeRenterId, b.EndDate), ct)).ToCreated(Route))
            .WithIdempotency(required: true)
            .WithSummary("Ký lại cho người còn ở: thanh lý HĐ cũ tại ngày bàn giao, tạo HĐ nháp mới từ ngày hôm sau (CT-UC-21)");
        group.MapPost("/{id:guid}/notice", async (Guid id, GiveNoticeRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GiveNoticeCommand(id, b.NoticeDate, b.PlannedMoveOutDate), ct)).ToHttp())
            .WithSummary("Báo trả phòng — trả cảnh báo nếu báo trước ít hơn thỏa thuận (thường 30 ngày)");

        // ---- Thanh lý
        group.MapPost("/{id:guid}/liquidation/start", async (Guid id, StartLiquidationRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new StartLiquidationCommand(id, b.ActualEndDate, b.Reason, b.Ground, b.Note), ct)).ToHttp())
            .WithSummary("Bắt đầu thanh lý (bên cho thuê đơn phương ⇒ phải nêu căn cứ Điều 172)");
        group.MapPost("/{id:guid}/liquidation/cancel", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CancelLiquidationCommand(id), ct)).ToHttp())
            .WithSummary("Hủy thanh lý, quay lại đang hiệu lực");
        group.MapPost("/{id:guid}/liquidation/complete", async (Guid id, CompleteLiquidationRequest? body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CompleteLiquidationCommand(id, body?.Settlement, body?.Method, body?.PaidAt, body?.Reason), ct)).ToHttp())
            .WithSummary("Hoàn tất thanh lý: cần phiếu quyết toán đã chốt; còn nợ ⇒ settlement CollectAll (đã thu toàn bộ) / WriteOff (bỏ nợ)");
        group.MapPost("/{id:guid}/final-invoice", async (Guid id, FinalInvoiceRequest? body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new CreateFinalInvoiceCommand(id, body?.FinalReadings), ct);
                return result.IsSuccess ? Results.Created($"{EndpointHelpers.ApiPrefix}/invoices/{result.Value!.Summary.Id}", result.Value) : result.ToHttp();
            })
            .WithIdempotency(required: false)
            .WithSummary("Lập phiếu quyết toán khi trả phòng (nhập chỉ số cuối) — nháp sửa được như phiếu thường");

        // ---- Tài sản bàn giao
        group.MapPost("/{id:guid}/assets", async (Guid id, AssetRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AddAssetCommand(id, b), ct)).ToCreated($"{Route}/{id}/assets"))
            .WithSummary("Thêm tài sản bàn giao (khi hợp đồng còn nháp)");
        group.MapPut("/{id:guid}/assets/{assetId:guid}", async (Guid id, Guid assetId, AssetRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateAssetCommand(id, assetId, b), ct)).ToHttp())
            .WithSummary("Sửa tài sản bàn giao");
        group.MapDelete("/{id:guid}/assets/{assetId:guid}", async (Guid id, Guid assetId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemoveAssetCommand(id, assetId), ct)).ToHttp())
            .WithSummary("Xóa tài sản bàn giao");
        group.MapPost("/{id:guid}/assets/{assetId:guid}/return", async (Guid id, Guid assetId, AssetReturnRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RecordAssetReturnCommand(id, assetId, b.ConditionAtReturn, b.CompensationValue), ct)).ToHttp())
            .WithSummary("Ghi tình trạng tài sản khi trả phòng + giá trị bồi thường");

        // ---- Xe gửi
        group.MapPost("/{id:guid}/vehicles", async (Guid id, RegisterVehicleRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RegisterVehicleCommand(id, b.RenterId, b.VehicleType, b.PlateNumber, b.BrandColor, b.RegisteredFrom, b.Note), ct))
                .ToCreated($"{Route}/{id}/vehicles"))
            .WithSummary("Đăng ký xe gửi (1 biển số chỉ đăng ký ở 1 nơi)");
        group.MapPost("/{id:guid}/vehicles/{vehicleId:guid}/end", async (Guid id, Guid vehicleId, EndVehicleRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new EndVehicleCommand(id, vehicleId, b.EndDate), ct)).ToHttp())
            .WithSummary("Kết thúc đăng ký xe");
    }
}

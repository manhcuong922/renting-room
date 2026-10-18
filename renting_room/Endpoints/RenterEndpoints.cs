using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Renters;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

/// <summary>Người thuê / người ở (M03 — phần cơ bản). Số giấy tờ luôn che, xem đầy đủ qua reveal có ghi log.</summary>
public static class RenterEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/renters";

    public static void MapRenterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithTags("Renters").RequireAuthorization(AuthPolicies.OrgMember);

        group.MapGet("/", async (string? q, Guid? propertyId, bool? staying, bool? includeArchived, int? page, int? pageSize, ISender sender,
                    CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchRentersQuery(q, null, null, page ?? 1, pageSize ?? Paging.DefaultPageSize, propertyId, staying,
                    includeArchived ?? false), ct)))
            .WithSummary("Danh sách / tìm người thuê theo tên (không dấu) / SĐT; lọc theo khu, đang ở / không còn ở, kèm hồ sơ đã ẩn. Tìm theo số giấy tờ: POST /renters/search");

        group.MapPost("/{id:guid}/archive", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ArchiveRenterCommand(id, true), ct)).ToHttp())
            .WithSummary("Ẩn hồ sơ khỏi danh sách / tìm kiếm mặc định (không xóa; người còn HĐ đang chạy thì không ẩn được)");
        group.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ArchiveRenterCommand(id, false), ct)).ToHttp())
            .WithSummary("Hiện lại hồ sơ đã ẩn");

        group.MapGet("/{id:guid}/consents", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListRenterConsentsQuery(id), ct)).ToHttp())
            .WithSummary("Các lần ghi nhận đồng ý xử lý dữ liệu của người thuê (mới nhất trước)");
        group.MapPost("/{id:guid}/consents", async (Guid id, AddConsentRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AddRenterConsentCommand(id, b.Purpose, b.Method, b.GivenAt, b.Note), ct)).ToHttp())
            .WithSummary("Ghi nhận đồng ý xử lý dữ liệu: mục đích, phương thức (ký giấy / điều khoản HĐ / miệng), ngày");
        group.MapPost("/{id:guid}/consents/{consentId:guid}/withdraw", async (Guid id, Guid consentId, WithdrawConsentRequest b, ISender sender,
                    CancellationToken ct) =>
                (await sender.Send(new WithdrawRenterConsentCommand(id, consentId, b.WithdrawnOn), ct)).ToHttp())
            .WithSummary("Ghi nhận người thuê rút lại đồng ý (giữ dòng, ghi ngày rút)");

        // POST nhưng chỉ đọc: không cần Idempotency-Key.
        group.MapPost("/search", async (SearchRentersRequest body, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchRentersQuery(
                    body.Q, body.IdNumber, body.IdType, body.Page ?? 1, body.PageSize ?? Paging.DefaultPageSize), ct)))
            .WithNoStore()
            .WithSummary("Tìm người thuê, gồm số giấy tờ khớp chính xác (số giấy tờ gửi trong body, không đặt trên URL)");

        group.MapPost("/", async (RenterInput body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateRenterCommand(body), ct)).ToCreated(Route))
            .WithIdempotency(required: true)
            .WithSummary("Tạo hồ sơ người thuê (409 nếu số giấy tờ đã có — dùng lại hồ sơ cũ)");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetRenterQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết người thuê");

        group.MapPut("/{id:guid}", async (Guid id, UpdateRenterRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateRenterCommand(id, body.Renter, body.Version), ct)).ToHttp())
            .WithSummary("Sửa hồ sơ (idNumber = null ⇒ giữ số giấy tờ cũ)");

        group.MapPost("/{id:guid}/reveal-id-number", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new RevealRenterIdNumberQuery(id), ct);
                return result.IsSuccess ? Results.Ok(new RevealedIdNumber(result.Value!)) : result.ToHttp();
            })
            .WithNoStore()
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithSummary("Xem số giấy tờ đầy đủ (có ghi audit; giới hạn theo user)");

        group.MapPost("/{id:guid}/anonymize", async (Guid id, AnonymizeRenterRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AnonymizeRenterCommand(id, body.Reason), ct)).ToHttp())
            .RequireAuthorization(AuthPolicies.OrgOwner)
            .WithSummary("Ẩn danh ngay (người thuê yêu cầu xóa dữ liệu / xóa sớm) — chỉ chủ trọ, lý do bắt buộc, KHÔNG đảo ngược");
    }
}

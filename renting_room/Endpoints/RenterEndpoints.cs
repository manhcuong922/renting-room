using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record UpdateRenterRequest(RenterInput Renter, uint Version);

/// <summary>Người thuê / người ở (M03 — phần cơ bản). Số giấy tờ luôn che, xem đầy đủ qua reveal có ghi log.</summary>
public static class RenterEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/renters";

    public static void MapRenterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithTags("Renters").RequireAuthorization(AuthPolicies.OrgMember);

        group.MapGet("/", async (string? q, string? idNumber, IdDocumentType? idType, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchRentersQuery(q, idNumber, idType, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Tìm người thuê theo tên (không dấu) / SĐT; số giấy tờ khớp chính xác");

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
            .WithSummary("Xem số giấy tờ đầy đủ (có ghi log)");
    }
}

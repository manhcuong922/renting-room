using Mediator;
using renting_room.Application.Audit;
using renting_room.Application.Common.Models;
using renting_room.Security;

namespace renting_room.Endpoints;

/// <summary>Nhật ký kiểm toán (C-10, ID-BR-19) — chỉ chủ trọ; có dữ liệu cá nhân nên không cache.</summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup($"{EndpointHelpers.ApiPrefix}/audit-logs").WithTags("Audit")
            .RequireAuthorization(AuthPolicies.OrgOwner).WithNoStore()
            .MapGet("/", async (string? entityType, Guid? entityId, Guid? userId, string? action, DateOnly? from, DateOnly? to,
                    int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListAuditLogsQuery(entityType, entityId, userId, action, from, to,
                    page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Nhật ký: ai đã làm gì, với đối tượng nào, lúc nào (lọc loại đối tượng, đối tượng, người, hành động, khoảng ngày) — mới nhất trước");
    }
}

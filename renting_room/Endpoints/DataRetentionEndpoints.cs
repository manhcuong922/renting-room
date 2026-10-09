using Mediator;
using renting_room.Application.Renters;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record UpdateDataRetentionRequest(int RetentionMonths, bool AutoAnonymize);

/// <summary>RT-BR-06: thời gian giữ dữ liệu cá nhân người thuê và bật / tắt ẩn danh tự động của tổ chức.</summary>
public static class DataRetentionEndpoints
{
    public static void MapDataRetentionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{EndpointHelpers.ApiPrefix}/org/data-retention").WithTags("Organization members");

        group.MapGet("/", async (ISender sender, CancellationToken ct) => Results.Ok(await sender.Send(new GetDataRetentionQuery(), ct)))
            .RequireAuthorization(AuthPolicies.OrgMember)
            .WithSummary("Thời gian giữ dữ liệu cá nhân người thuê (tháng) và trạng thái ẩn danh tự động");

        group.MapPut("/", async (UpdateDataRetentionRequest body, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new UpdateDataRetentionCommand(body.RetentionMonths, body.AutoAnonymize), ct)))
            .RequireAuthorization(AuthPolicies.OrgOwner)
            .WithSummary("Đặt thời gian giữ (36–120 tháng) / tắt ẩn danh tự động — chỉ chủ trọ; tắt ⇒ cảnh báo AUTO_ANONYMIZE_DISABLED");
    }
}

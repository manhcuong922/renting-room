using Mediator;
using renting_room.Application.Exports;
using renting_room.Security;

namespace renting_room.Endpoints;

/// <summary>Xuất Excel (M10). POST vì bộ lọc là các danh sách id; không tạo dữ liệu nên không cần Idempotency-Key.</summary>
public static class ExportEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/exports";
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithTags("Exports").RequireAuthorization(AuthPolicies.OrgMember).WithNoStore();

        group.MapPost("/renters", async (ExportRentersQuery body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(body, ct);
                return result.IsSuccess
                    ? Results.File(result.Value!.Content, XlsxContentType, result.Value.FileName)
                    : result.ToHttp();
            })
            .Produces(StatusCodes.Status200OK, contentType: XlsxContentType)
            .WithSummary("Xuất Excel danh sách người thuê — lọc theo khu, tầng, nhóm phòng, phòng, khoảng ngày");
    }
}

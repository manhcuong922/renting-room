using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using renting_room.Application.Exports;
using renting_room.Http;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>Xuất Excel (M10). POST vì bộ lọc là các danh sách id; không tạo dữ liệu nên không cần Idempotency-Key.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Exports")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
[NoStore]
public sealed class ExportsController(ISender sender) : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpPost("exports/renters")]
    // Mỗi lần xuất tới 20.000 dòng (có thể kèm số giấy tờ đầy đủ) ⇒ dùng hạn mức chặt "sensitive" theo user.
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [EndpointSummary("Xuất Excel danh sách người thuê — lọc theo khu, tầng, nhóm phòng, phòng, khoảng ngày")]
    public async Task<IResult> ExportRenters(ExportRentersQuery body, CancellationToken ct)
    {
        var result = await sender.Send(body, ct);
        return result.IsSuccess
            ? Results.File(result.Value!.Content, XlsxContentType, result.Value.FileName)
            : result.ToHttp();
    }
}

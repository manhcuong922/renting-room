using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Audit;
using renting_room.Application.Common.Models;
using renting_room.Http;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>Nhật ký kiểm toán (C-10, ID-BR-19) — chỉ chủ trọ; có dữ liệu cá nhân nên không cache.</summary>
[ApiController]
[Route("api/v1")]
[Tags("Audit")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgOwner)]
[NoStore]
public sealed class AuditLogsController(ISender sender) : ControllerBase
{
    [HttpGet("audit-logs")]
    [EndpointSummary("Nhật ký: ai đã làm gì, với đối tượng nào, lúc nào (lọc loại đối tượng, đối tượng, người, hành động, khoảng ngày) — mới nhất trước")]
    public async Task<IResult> List(
        string? entityType, Guid? entityId, Guid? userId, string? action, DateOnly? from, DateOnly? to,
        int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListAuditLogsQuery(entityType, entityId, userId, action, from, to,
            page ?? 1, pageSize ?? Paging.DefaultPageSize), ct));
}

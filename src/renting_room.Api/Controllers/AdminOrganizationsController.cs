using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Organizations.ChangeStatus;
using renting_room.Application.Identity.Organizations.CreateOrganization;
using renting_room.Application.Identity.Organizations.Queries;
using renting_room.Errors;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Admin - Organizations")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.SystemAdmin)]
public sealed class AdminOrganizationsController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/admin/organizations";

    [HttpGet("admin/organizations")]
    [EndpointSummary("Danh sách tổ chức chủ trọ (phân trang)")]
    public async Task<Ok<PagedResult<OrganizationSummaryDto>>> List(
        [FromQuery] ListOrganizationsRequest request, CancellationToken cancellationToken)
    {
        var query = new ListOrganizationsQuery(
            request.Search,
            request.Status,
            request.Page ?? 1,
            request.PageSize ?? Paging.DefaultPageSize);

        return TypedResults.Ok(await sender.Send(query, cancellationToken));
    }

    [HttpPost("admin/organizations")]
    [Idempotent] // gửi lại không tạo trùng, và trả lại đúng mật khẩu tạm của lần đầu
    [NoStore]
    [EndpointSummary("Tạo tổ chức + tài khoản chủ trọ; trả mật khẩu tạm đúng một lần")]
    public async Task<Results<Created<CreateOrganizationResult>, ProblemHttpResult>> Create(
        CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"{Route}/{result.Value!.OrganizationId}", result.Value)
            : result.Error!.ToProblem();
    }

    [HttpGet("admin/organizations/{id:guid}")]
    [EndpointSummary("Chi tiết tổ chức")]
    public async Task<Results<Ok<OrganizationDetailDto>, ProblemHttpResult>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrganizationQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    [HttpPost("admin/organizations/{id:guid}/suspend")]
    [Idempotent(required: false)]
    [EndpointSummary("Tạm ngưng tổ chức (thu hồi mọi phiên đăng nhập)")]
    public async Task<Results<NoContent, ProblemHttpResult>> Suspend(
        Guid id, SuspendOrganizationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SuspendOrganizationCommand(id, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    [HttpPost("admin/organizations/{id:guid}/reactivate")]
    [Idempotent(required: false)]
    [EndpointSummary("Kích hoạt lại tổ chức")]
    public async Task<Results<NoContent, ProblemHttpResult>> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReactivateOrganizationCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }
}

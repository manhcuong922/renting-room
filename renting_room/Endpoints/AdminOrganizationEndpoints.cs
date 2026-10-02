using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using renting_room.Application.Common.Models;
using renting_room.Application.Identity.Organizations.ChangeStatus;
using renting_room.Application.Identity.Organizations.CreateOrganization;
using renting_room.Application.Identity.Organizations.Queries;
using renting_room.Domain.Identity;
using renting_room.Errors;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record ListOrganizationsRequest(string? Search, OrganizationStatus? Status, int? Page, int? PageSize);

public sealed record SuspendOrganizationRequest(string Reason);

public static class AdminOrganizationEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/admin/organizations";

    public static void MapAdminOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .WithTags("Admin - Organizations")
            .RequireAuthorization(AuthPolicies.SystemAdmin);

        group.MapGet("/", List).WithSummary("Danh sách tổ chức chủ trọ (phân trang)");
        group.MapPost("/", Create)
            .WithIdempotency(required: true) // gửi lại không tạo trùng, và trả lại đúng mật khẩu tạm của lần đầu
            .WithNoStore()
            .WithSummary("Tạo tổ chức + tài khoản chủ trọ; trả mật khẩu tạm đúng một lần");
        group.MapGet("/{id:guid}", Get).WithSummary("Chi tiết tổ chức");
        group.MapPost("/{id:guid}/suspend", Suspend).WithIdempotency(required: false).WithSummary("Tạm ngưng tổ chức (thu hồi mọi phiên đăng nhập)");
        group.MapPost("/{id:guid}/reactivate", Reactivate).WithIdempotency(required: false).WithSummary("Kích hoạt lại tổ chức");
    }

    private static async Task<Ok<PagedResult<OrganizationSummaryDto>>> List(
        [AsParameters] ListOrganizationsRequest request, ISender sender, CancellationToken cancellationToken)
    {
        var query = new ListOrganizationsQuery(
            request.Search,
            request.Status,
            request.Page ?? 1,
            request.PageSize ?? Paging.DefaultPageSize);

        return TypedResults.Ok(await sender.Send(query, cancellationToken));
    }

    private static async Task<Results<Created<CreateOrganizationResult>, ProblemHttpResult>> Create(
        CreateOrganizationCommand command, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"{Route}/{result.Value!.OrganizationId}", result.Value)
            : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<OrganizationDetailDto>, ProblemHttpResult>> Get(
        Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrganizationQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Error!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Suspend(
        Guid id, SuspendOrganizationRequest request, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SuspendOrganizationCommand(id, request.Reason), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Reactivate(
        Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReactivateOrganizationCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }
}

using Mediator;
using renting_room.Application.Identity.Members;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record CreateManagerRequest(string FullName, string? Phone, string? Email);

public sealed record UpdateManagerRequest(string FullName, string? Phone, string? Email, uint Version);

public sealed record SensitiveDataAccessRequest(bool Allowed);

public sealed record WriteOffPermissionRequest(bool Allowed);

/// <summary>Thành viên tổ chức: chủ trọ + phó quản lý (M01 §3.3).</summary>
public static class MemberEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/org/members";

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup(Route).WithTags("Organization members");

        members.MapGet("/", async (bool? includeRemoved, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListMembersQuery(includeRemoved ?? false), ct)))
            .RequireAuthorization(AuthPolicies.OrgMember)
            .WithSummary("Danh sách chủ trọ và phó quản lý");

        members.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetMemberQuery(id), ct)).ToHttp())
            .RequireAuthorization(AuthPolicies.OrgMember)
            .WithSummary("Chi tiết thành viên");

        var ownerOnly = members.MapGroup("").RequireAuthorization(AuthPolicies.OrgOwner);

        ownerOnly.MapPost("/", async (CreateManagerRequest body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new CreateManagerCommand(body.FullName, body.Phone, body.Email), ct);
                return result.IsSuccess
                    ? Results.Created($"{Route}/{result.Value!.UserId}", result.Value)
                    : result.ToHttp();
            })
            .WithIdempotency(required: true)
            .WithNoStore()
            .WithSummary("Thêm phó quản lý — trả mật khẩu tạm một lần (hết hạn sau 72 giờ)");

        ownerOnly.MapPut("/{id:guid}", async (Guid id, UpdateManagerRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateManagerCommand(id, body.FullName, body.Phone, body.Email, body.Version), ct)).ToHttp())
            .WithSummary("Sửa thông tin phó quản lý");

        ownerOnly.MapPost("/{id:guid}/lock", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeManagerStatusCommand(id, ManagerAction.Lock), ct)).ToHttp())
            .WithSummary("Khóa phó quản lý (đăng xuất ngay mọi phiên)");

        ownerOnly.MapPost("/{id:guid}/unlock", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeManagerStatusCommand(id, ManagerAction.Unlock), ct)).ToHttp())
            .WithSummary("Mở khóa phó quản lý");

        ownerOnly.MapPost("/{id:guid}/remove", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeManagerStatusCommand(id, ManagerAction.Remove), ct)).ToHttp())
            .WithSummary("Gỡ phó quản lý khỏi tổ chức (vĩnh viễn)");

        ownerOnly.MapPut("/{id:guid}/sensitive-data-access", async (Guid id, SensitiveDataAccessRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetSensitiveDataAccessCommand(id, body.Allowed), ct)).ToHttp())
            .WithSummary("Cấp / thu hồi quyền xem dữ liệu nhạy cảm (số giấy tờ đầy đủ) cho phó quản lý");

        ownerOnly.MapPut("/{id:guid}/write-off-permission", async (Guid id, WriteOffPermissionRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetWriteOffPermissionCommand(id, body.Allowed), ct)).ToHttp())
            .WithSummary("Cấp / thu hồi quyền bỏ nợ cho từng phó quản lý (mặc định không)");

        ownerOnly.MapPost("/{id:guid}/reset-password", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ResetManagerPasswordCommand(id), ct)).ToHttp())
            .WithNoStore()
            .WithSummary("Cấp lại mật khẩu tạm cho phó quản lý");
    }
}

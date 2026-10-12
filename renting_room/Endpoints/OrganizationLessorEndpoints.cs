using Mediator;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Properties;
using renting_room.Security;

namespace renting_room.Endpoints;

/// <summary>ID-UC-03: chủ trọ tự sửa thông tin tổ chức (mã tổ chức không đổi).</summary>
public sealed record UpdateOrganizationInfoRequest(
    string Name, string? ContactName, string? ContactPhone, string? ContactEmail, string? TaxCode, string? Address);

/// <summary>PR-BR-17: bên cho thuê mặc định = thông tin chủ trọ, khai một lần cho mọi khu không khai riêng.</summary>
public static class OrganizationLessorEndpoints
{
    public static void MapOrganizationLessorEndpoints(this IEndpointRouteBuilder app)
    {
        var organization = app.MapGroup($"{EndpointHelpers.ApiPrefix}/org").WithTags("Organization members");
        organization.MapGet("/", async (ISender sender, CancellationToken ct) => Results.Ok(await sender.Send(new GetOrganizationInfoQuery(), ct)))
            .RequireAuthorization(AuthPolicies.OrgMember)
            .WithSummary("Thông tin tổ chức (tên, liên hệ, mã số thuế, địa chỉ)");
        organization.MapPut("/", async (UpdateOrganizationInfoRequest b, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new UpdateOrganizationInfoCommand(b.Name, b.ContactName, b.ContactPhone, b.ContactEmail, b.TaxCode, b.Address), ct)))
            .RequireAuthorization(AuthPolicies.OrgOwner)
            .WithSummary("Chủ trọ sửa thông tin tổ chức — mã tổ chức không đổi");

        var group = app.MapGroup($"{EndpointHelpers.ApiPrefix}/org/lessor").WithTags("Organization members");

        group.MapGet("/", async (ISender sender, CancellationToken ct) => Results.Ok(await sender.Send(new GetOrganizationLessorQuery(), ct)))
            .RequireAuthorization(AuthPolicies.OrgMember)
            .WithSummary("Thông tin chủ trọ làm bên cho thuê (lessor = null ⇒ chưa khai; prefill = gợi ý từ thông tin liên hệ)");

        group.MapPut("/", async (UpdateLessorRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateOrganizationLessorCommand(
                    b.Type, b.Name, b.Address, b.Phone, b.Email, b.IdType, b.IdNumber, b.IdIssueDate, b.IdIssuePlace,
                    b.DateOfBirth, b.TaxCode, b.RepresentativeName, b.RepresentativeTitle, b.AuthorizationDocNo, b.AuthorizationDocDate), ct)).ToHttp())
            .RequireAuthorization(AuthPolicies.OrgOwner)
            .WithSummary("Khai thông tin chủ trọ làm bên cho thuê — chỉ chủ trọ. idNumber = null ⇒ giữ số cũ");

        group.MapPost("/reveal-id-number", async (ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new RevealOrganizationLessorIdNumberQuery(), ct);
                return result.IsSuccess ? Results.Ok(new RevealedIdNumber(result.Value!)) : result.ToHttp();
            })
            .RequireAuthorization(AuthPolicies.OrgMember)
            .WithNoStore()
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithSummary("Xem số giấy tờ đầy đủ của chủ trọ (có ghi audit; giới hạn theo user)");
    }
}

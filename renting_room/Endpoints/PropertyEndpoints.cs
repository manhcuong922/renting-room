using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record UpdatePropertyRequest(
    string Name,
    AddressInput Address,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput? Land,
    BillingDefaultsInput BillingDefaults,
    uint Version);

public sealed record UpdateLessorRequest(
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    string? IdNumber,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate);

public sealed record UpdateBankAccountRequest(string? BankName, string? AccountNo, string? AccountName);

public sealed record UpdateHouseRulesRequest(string? Text);

public sealed record RevealedIdNumber(string IdNumber);

/// <summary>Khu trọ (M02) — chủ trọ và phó quản lý.</summary>
public static class PropertyEndpoints
{
    private const string Route = $"{EndpointHelpers.ApiPrefix}/properties";

    public static void MapPropertyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithTags("Properties").RequireAuthorization(AuthPolicies.OrgMember);

        group.MapGet("/", async (string? search, bool? includeArchived, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new ListPropertiesQuery(search, includeArchived ?? false, page ?? 1, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithSummary("Danh sách khu trọ (kèm số phòng, số phòng đang thuê)");

        group.MapPost("/", async (CreatePropertyCommand command, ISender sender, CancellationToken ct) =>
                (await sender.Send(command, ct)).ToCreated(Route))
            .WithIdempotency(required: true)
            .WithSummary("Tạo khu trọ");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetPropertyQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết khu trọ (số giấy tờ bên cho thuê đã che)");

        group.MapPut("/{id:guid}", async (Guid id, UpdatePropertyRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdatePropertyCommand(
                    id, body.Name, body.Address, body.Description, body.EvnCustomerCode, body.Land, body.BillingDefaults, body.Version), ct)).ToHttp())
            .WithSummary("Sửa thông tin chung + cài đặt thu mặc định (không ảnh hưởng hợp đồng đã tạo)");

        group.MapPost("/{id:guid}/archive", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ArchivePropertyCommand(id), ct)).ToHttp())
            .WithSummary("Ngừng sử dụng khu (cùng toàn bộ phòng)");

        group.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RestorePropertyCommand(id), ct)).ToHttp())
            .WithSummary("Khôi phục khu");

        group.MapPut("/{id:guid}/lessor", async (Guid id, UpdateLessorRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateLessorCommand(
                    id, b.Type, b.Name, b.Address, b.Phone, b.Email, b.IdType, b.IdNumber, b.IdIssueDate, b.IdIssuePlace,
                    b.DateOfBirth, b.TaxCode, b.RepresentativeName, b.RepresentativeTitle, b.AuthorizationDocNo, b.AuthorizationDocDate), ct)).ToHttp())
            .WithSummary("Khai báo bên cho thuê (bắt buộc trước khi kích hoạt hợp đồng). idNumber = null ⇒ giữ số cũ");

        group.MapPost("/{id:guid}/lessor/reveal-id-number", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new RevealLessorIdNumberQuery(id), ct);
                return result.IsSuccess ? Results.Ok(new RevealedIdNumber(result.Value!)) : result.ToHttp();
            })
            .WithNoStore()
            .WithSummary("Xem số giấy tờ đầy đủ của bên cho thuê (có ghi log)");

        group.MapPut("/{id:guid}/bank-account", async (Guid id, UpdateBankAccountRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateBankAccountCommand(id, body.BankName, body.AccountNo, body.AccountName), ct)).ToHttp())
            .WithSummary("Tài khoản nhận tiền (gửi cả 3 trường null để xóa)");

        group.MapPut("/{id:guid}/house-rules", async (Guid id, UpdateHouseRulesRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateHouseRulesCommand(id, body.Text), ct)).ToHttp())
            .WithSummary("Nội quy khu trọ (đính kèm hợp đồng khi kích hoạt)");
    }
}

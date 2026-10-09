using Mediator;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record CreateFeeTypeRequest(
    string Name, FeeGroup Group, ChargeBasis? ChargeBasis, string Unit, bool AutoAttach, decimal? DefaultQuantity, int? SortOrder,
    FeePriceInput? InitialPrice, VehicleType? VehicleType = null);

public sealed record UpdateFeeTypeRequest(
    string Name, string Unit, bool AutoAttach, decimal? DefaultQuantity, int SortOrder, uint Version, VehicleType? VehicleType = null);

public sealed record CopyFeeCatalogRequest(bool? IncludePrices, DateOnly? EffectiveFrom);

public sealed record BulkFeeUsageRequest(FeeUsageAction Action, IReadOnlyList<Guid> ContractIds, decimal? Quantity, decimal? UnitPriceOverride);

/// <summary>Khoản thu & bảng giá của khu (M04): điện nước theo công tơ; dịch vụ theo phòng / đầu người / số gói (giữ xe…).</summary>
public static class FeeEndpoints
{
    private const string PropertiesRoute = $"{EndpointHelpers.ApiPrefix}/properties";
    private const string Route = $"{EndpointHelpers.ApiPrefix}/fee-types";

    public static void MapFeeEndpoints(this IEndpointRouteBuilder app)
    {
        var byProperty = app.MapGroup($"{PropertiesRoute}/{{propertyId:guid}}/fee-types").WithTags("Fee types")
            .RequireAuthorization(AuthPolicies.OrgMember);

        byProperty.MapGet("/", async (Guid propertyId, bool? includeArchived, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListFeeTypesQuery(propertyId, includeArchived ?? false), ct)).ToHttp())
            .WithSummary("Danh sách khoản thu của khu (mảng) — kèm giá đang hiệu lực và lịch sử giá");

        byProperty.MapPost("/", async (Guid propertyId, CreateFeeTypeRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateFeeTypeCommand(propertyId, b.Name, b.Group, b.ChargeBasis, b.Unit, b.AutoAttach,
                    b.DefaultQuantity, b.SortOrder, b.InitialPrice, b.VehicleType), ct)).ToCreated(Route))
            .WithIdempotency(required: true)
            .WithSummary("Tạo khoản thu (theo chỉ số / cố định theo phòng hoặc theo người / theo số lượng)");

        byProperty.MapPost("/copy-from/{sourcePropertyId:guid}", async (Guid propertyId, Guid sourcePropertyId, CopyFeeCatalogRequest? b,
                ISender sender, CancellationToken ct) =>
                (await sender.Send(new CopyFeeCatalogCommand(sourcePropertyId, propertyId, b?.IncludePrices ?? true, b?.EffectiveFrom), ct)).ToHttp())
            .WithSummary("Sao chép danh mục khoản thu từ khu khác (trùng tên bỏ qua)");

        var fees = app.MapGroup(Route).WithTags("Fee types").RequireAuthorization(AuthPolicies.OrgMember);

        fees.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetFeeTypeQuery(id), ct)).ToHttp())
            .WithSummary("Chi tiết khoản thu + lịch sử giá");

        fees.MapPut("/{id:guid}", async (Guid id, UpdateFeeTypeRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateFeeTypeCommand(id, b.Name, b.Unit, b.AutoAttach, b.DefaultQuantity, b.SortOrder, b.Version, b.VehicleType), ct)).ToHttp())
            .WithSummary("Sửa tên / đơn vị / tự gắn / thứ tự (không đổi được cách tính)");

        fees.MapGet("/{id:guid}/usage", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetFeeUsageQuery(id), ct)).ToHttp())
            .WithSummary("FE-UC-08: phòng của khu đang dùng / chưa dùng khoản thu này (số gói, giá riêng, từ kỳ nào)");

        fees.MapPost("/{id:guid}/usage", async (Guid id, BulkFeeUsageRequest b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new BulkContractFeeCommand(id, b.Action, b.ContractIds, b.Quantity, b.UnitPriceOverride), ct)).ToHttp())
            .WithSummary("FE-UC-08: thêm / bớt khoản thu cho nhiều HĐ một lúc — từ kỳ chưa chốt đầu tiên của từng HĐ, trả kết quả từng HĐ");

        fees.MapPost("/{id:guid}/archive", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeFeeTypeStateCommand(id, Archive: true), ct)).ToHttp())
            .WithSummary("Ngừng dùng (khi không còn hợp đồng đang gắn)");

        fees.MapPost("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ChangeFeeTypeStateCommand(id, Archive: false), ct)).ToHttp())
            .WithSummary("Khôi phục khoản thu");

        fees.MapPost("/{id:guid}/prices", async (Guid id, FeePriceInput b, ISender sender, CancellationToken ct) =>
                (await sender.Send(new AddFeePriceCommand(id, b), ct)).ToCreated($"{Route}/{id}/prices"))
            .WithSummary("Thêm bảng giá mới (đơn giá hoặc bậc thang) từ ngày hiệu lực");

        fees.MapDelete("/{id:guid}/prices/{priceId:guid}", async (Guid id, Guid priceId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new DeleteFeePriceCommand(id, priceId), ct)).ToHttp())
            .WithSummary("Xóa bản giá (chưa thuộc kỳ đã chốt phiếu)");
    }
}

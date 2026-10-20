using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using renting_room.Application.Contracts;
using renting_room.Application.Fees;
using renting_room.Requests;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Controllers;

[ApiController]
[Route("api/v1")]
[Tags("Fee types")]
[Consumes("application/json")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class FeeTypesController(ISender sender) : ControllerBase
{
    private const string Route = "/api/v1/fee-types";

    [HttpGet("properties/{propertyId:guid}/fee-types")]
    [EndpointSummary("Danh sách khoản thu của khu (mảng) — kèm giá đang hiệu lực và lịch sử giá")]
    public async Task<IResult> List(Guid propertyId, bool? includeArchived, CancellationToken ct) =>
        (await sender.Send(new ListFeeTypesQuery(propertyId, includeArchived ?? false), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/fee-types")]
    [Idempotent]
    [EndpointSummary("Tạo khoản thu (theo chỉ số / cố định theo phòng hoặc theo người / theo số lượng)")]
    public async Task<IResult> Create(Guid propertyId, CreateFeeTypeRequest b, CancellationToken ct) =>
        (await sender.Send(new CreateFeeTypeCommand(propertyId, b.Name, b.Group, b.ChargeBasis, b.Unit, b.AutoAttach,
            b.DefaultQuantity, b.SortOrder, b.InitialPrice, b.VehicleType), ct)).ToCreated(Route);

    [HttpPost("properties/{propertyId:guid}/fee-types/copy-from/{sourcePropertyId:guid}")]
    [EndpointSummary("Sao chép danh mục khoản thu từ khu khác (trùng tên bỏ qua)")]
    public async Task<IResult> CopyFrom(
        Guid propertyId, Guid sourcePropertyId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CopyFeeCatalogRequest? b,
        CancellationToken ct) =>
        (await sender.Send(new CopyFeeCatalogCommand(sourcePropertyId, propertyId, b?.IncludePrices ?? true, b?.EffectiveFrom), ct)).ToHttp();

    [HttpGet("fee-types/{id:guid}")]
    [EndpointSummary("Chi tiết khoản thu + lịch sử giá")]
    public async Task<IResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetFeeTypeQuery(id), ct)).ToHttp();

    [HttpPut("fee-types/{id:guid}")]
    [EndpointSummary("Sửa tên / đơn vị / tự gắn / thứ tự (không đổi được cách tính)")]
    public async Task<IResult> Update(Guid id, UpdateFeeTypeRequest b, CancellationToken ct) =>
        (await sender.Send(new UpdateFeeTypeCommand(id, b.Name, b.Unit, b.AutoAttach, b.DefaultQuantity, b.SortOrder, b.Version, b.VehicleType), ct)).ToHttp();

    [HttpGet("fee-types/{id:guid}/usage")]
    [EndpointSummary("FE-UC-08: phòng của khu đang dùng / chưa dùng khoản thu này (số gói, giá riêng, từ kỳ nào)")]
    public async Task<IResult> GetUsage(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetFeeUsageQuery(id), ct)).ToHttp();

    [HttpPost("fee-types/{id:guid}/usage")]
    [EndpointSummary("FE-UC-08: thêm / bớt khoản thu cho nhiều HĐ một lúc — từ kỳ chưa chốt đầu tiên của từng HĐ, trả kết quả từng HĐ")]
    public async Task<IResult> ChangeUsage(Guid id, BulkFeeUsageRequest b, CancellationToken ct) =>
        (await sender.Send(new BulkContractFeeCommand(id, b.Action, b.ContractIds, b.Quantity, b.UnitPriceOverride), ct)).ToHttp();

    [HttpPost("fee-types/{id:guid}/archive")]
    [EndpointSummary("Ngừng dùng (khi không còn hợp đồng đang gắn)")]
    public async Task<IResult> Archive(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeFeeTypeStateCommand(id, Archive: true), ct)).ToHttp();

    [HttpPost("fee-types/{id:guid}/restore")]
    [EndpointSummary("Khôi phục khoản thu")]
    public async Task<IResult> Restore(Guid id, CancellationToken ct) =>
        (await sender.Send(new ChangeFeeTypeStateCommand(id, Archive: false), ct)).ToHttp();

    [HttpPost("fee-types/{id:guid}/prices")]
    [EndpointSummary("Thêm bảng giá mới (đơn giá hoặc bậc thang) từ ngày hiệu lực")]
    public async Task<IResult> AddPrice(Guid id, FeePriceInput b, CancellationToken ct) =>
        (await sender.Send(new AddFeePriceCommand(id, b), ct)).ToCreated($"{Route}/{id}/prices");

    [HttpDelete("fee-types/{id:guid}/prices/{priceId:guid}")]
    [EndpointSummary("Xóa bản giá (chưa thuộc kỳ đã chốt phiếu)")]
    public async Task<IResult> DeletePrice(Guid id, Guid priceId, CancellationToken ct) =>
        (await sender.Send(new DeleteFeePriceCommand(id, priceId), ct)).ToHttp();
}

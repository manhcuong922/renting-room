using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using renting_room.Application.Exports;
using renting_room.Application.Imports;
using renting_room.Domain.Common;
using renting_room.Http;
using renting_room.Idempotency;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>
/// Import Excel (PR-UC-11 phòng, RT-UC-10 người thuê đang ở): tải mẫu → tải file lên xem trước (đọc + kiểm, server không giữ gì) → sửa trên
/// màn → gửi lại các dòng (JSON) để lưu phần hợp lệ. Chỉ chủ trọ. Upload multipart field <c>file</c>; API dùng Bearer token nên không cần
/// chống CSRF. Server bỏ qua mọi id do client gửi (tra theo mã phòng / số giấy tờ).
/// </summary>
[ApiController]
[Route("api/v1")]
[Tags("Imports")]
[Authorize(Policy = AuthPolicies.OrgOwner)]
[NoStore]
public sealed class ImportsController(ISender sender) : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxRequestBytes = 2 * 1024 * 1024 + 64 * 1024; // file 2 MB + phần bao multipart

    [HttpGet("properties/{propertyId:guid}/rooms/import-template")]
    [EndpointSummary("PR-UC-11: tải file mẫu nhập phòng của khu")]
    public async Task<IResult> GetRoomTemplate(Guid propertyId, CancellationToken ct) =>
        File(await sender.Send(new GetRoomImportTemplateQuery(propertyId), ct));

    [HttpPost("properties/{propertyId:guid}/rooms/import/preview")]
    [Consumes("multipart/form-data")]
    [RequireAntiforgeryToken(false)]
    [RequestSizeLimit(MaxRequestBytes)]
    [EnableRateLimiting(RateLimitPolicies.Import)]
    [EndpointSummary("PR-UC-11: đọc file, kiểm từng dòng — chưa lưu, server không giữ gì")]
    public async Task<IResult> PreviewRooms(Guid propertyId, IFormFile file, CancellationToken ct) =>
        (await sender.Send(new PreviewRoomImportCommand(propertyId, await ReadAsync(file, ct)), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/rooms/import")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBytes)]
    [Idempotent]
    [EndpointSummary("PR-UC-11: kiểm lại + lưu các dòng hợp lệ (mỗi dòng 1 transaction), trả kết quả từng dòng")]
    public async Task<IResult> SaveRooms(Guid propertyId, SaveRoomImportRequest body, CancellationToken ct) =>
        (await sender.Send(new SaveRoomImportCommand(propertyId, body.Rows), ct)).ToHttp();

    [HttpGet("properties/{propertyId:guid}/tenancies/import-template")]
    [EndpointSummary("RT-UC-10: tải file mẫu nhập người thuê đang ở (1 sheet, mỗi dòng 1 người)")]
    public async Task<IResult> GetTenancyTemplate(Guid propertyId, CancellationToken ct) =>
        File(await sender.Send(new GetTenancyImportTemplateQuery(propertyId), ct));

    [HttpPost("properties/{propertyId:guid}/tenancies/import/preview")]
    [Consumes("multipart/form-data")]
    [RequireAntiforgeryToken(false)]
    [RequestSizeLimit(MaxRequestBytes)]
    [EnableRateLimiting(RateLimitPolicies.Import)]
    [EndpointSummary("RT-UC-10: đọc file, kiểm từng người + từng phòng — chưa lưu, server không giữ gì")]
    public async Task<IResult> PreviewTenancies(Guid propertyId, IFormFile file, CancellationToken ct) =>
        (await sender.Send(new PreviewTenancyImportCommand(propertyId, await ReadAsync(file, ct)), ct)).ToHttp();

    [HttpPost("properties/{propertyId:guid}/tenancies/import")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBytes)]
    [Idempotent]
    [EndpointSummary("RT-UC-10: kiểm lại + lưu các phòng hợp lệ (mỗi phòng 1 transaction), trả kết quả từng phòng")]
    public async Task<IResult> SaveTenancies(Guid propertyId, SaveTenancyImportRequest body, CancellationToken ct) =>
        (await sender.Send(new SaveTenancyImportCommand(propertyId, body.Rows), ct)).ToHttp();

    private static IResult File(Result<ExportFile> result) =>
        result.IsSuccess ? Results.File(result.Value!.Content, XlsxContentType, result.Value.FileName) : result.ToHttp();

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        // Đọc tối đa 2 MB + 1 khối ⇒ file lớn hơn bị handler từ chối (IMPORT_FILE_TOO_LARGE) mà không nạp hết vào bộ nhớ.
        await using var stream = file.OpenReadStream();
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0 && buffer.Length <= 2 * 1024 * 1024)
            buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }
}

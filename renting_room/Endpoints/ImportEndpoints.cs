using Mediator;
using Microsoft.AspNetCore.Mvc;
using renting_room.Application.Imports;
using renting_room.Domain.Common;
using renting_room.Idempotency;
using renting_room.Security;

namespace renting_room.Endpoints;

public sealed record SaveRoomImportRequest(IReadOnlyList<RoomImportRow> Rows);

public sealed record SaveTenancyImportRequest(IReadOnlyList<TenancyImportRow> Rows);

/// <summary>
/// Import Excel (PR-UC-11 phòng, RT-UC-10 người thuê đang ở): tải mẫu → tải file lên xem trước (đọc + kiểm, server không giữ gì) → sửa trên
/// màn → gửi lại các dòng (JSON) để lưu phần hợp lệ. Chỉ chủ trọ. Upload multipart field <c>file</c>; API dùng Bearer token nên không cần
/// chống CSRF. Server bỏ qua mọi id do client gửi (tra theo mã phòng / số giấy tờ).
/// </summary>
public static class ImportEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxRequestBytes = 2 * 1024 * 1024 + 64 * 1024; // file 2 MB + phần bao multipart

    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{EndpointHelpers.ApiPrefix}/properties/{{propertyId:guid}}").WithTags("Imports")
            .RequireAuthorization(AuthPolicies.OrgOwner).WithNoStore();

        group.MapGet("/rooms/import-template", async (Guid propertyId, ISender sender, CancellationToken ct) =>
                File(await sender.Send(new GetRoomImportTemplateQuery(propertyId), ct)))
            .Produces(StatusCodes.Status200OK, contentType: XlsxContentType)
            .WithSummary("PR-UC-11: tải file mẫu nhập phòng của khu");
        Preview(group.MapPost("/rooms/import/preview", async (Guid propertyId, IFormFile file, ISender sender, CancellationToken ct) =>
                (await sender.Send(new PreviewRoomImportCommand(propertyId, await ReadAsync(file, ct)), ct)).ToHttp()))
            .WithSummary("PR-UC-11: đọc file, kiểm từng dòng — chưa lưu, server không giữ gì");
        Save(group.MapPost("/rooms/import", async (Guid propertyId, SaveRoomImportRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SaveRoomImportCommand(propertyId, body.Rows), ct)).ToHttp()))
            .WithSummary("PR-UC-11: kiểm lại + lưu các dòng hợp lệ (mỗi dòng 1 transaction), trả kết quả từng dòng");

        group.MapGet("/tenancies/import-template", async (Guid propertyId, ISender sender, CancellationToken ct) =>
                File(await sender.Send(new GetTenancyImportTemplateQuery(propertyId), ct)))
            .Produces(StatusCodes.Status200OK, contentType: XlsxContentType)
            .WithSummary("RT-UC-10: tải file mẫu nhập người thuê đang ở (1 sheet, mỗi dòng 1 người)");
        Preview(group.MapPost("/tenancies/import/preview", async (Guid propertyId, IFormFile file, ISender sender, CancellationToken ct) =>
                (await sender.Send(new PreviewTenancyImportCommand(propertyId, await ReadAsync(file, ct)), ct)).ToHttp()))
            .WithSummary("RT-UC-10: đọc file, kiểm từng người + từng phòng — chưa lưu, server không giữ gì");
        Save(group.MapPost("/tenancies/import", async (Guid propertyId, SaveTenancyImportRequest body, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SaveTenancyImportCommand(propertyId, body.Rows), ct)).ToHttp()))
            .WithSummary("RT-UC-10: kiểm lại + lưu các phòng hợp lệ (mỗi phòng 1 transaction), trả kết quả từng phòng");
    }

    private static RouteHandlerBuilder Preview(RouteHandlerBuilder builder) => builder
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
        .RequireRateLimiting(RateLimitPolicies.Import);

    private static RouteHandlerBuilder Save(RouteHandlerBuilder builder) => builder
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
        .WithIdempotency(required: true);

    private static IResult File(Result<Application.Exports.ExportFile> result) =>
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

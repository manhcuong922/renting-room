using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using renting_room.Application.Attachments;
using renting_room.Domain.Attachments;
using renting_room.Errors;
using renting_room.Http;
using renting_room.Requests;
using renting_room.Security;

namespace renting_room.Controllers;

/// <summary>M09 — ảnh / tài liệu đính kèm. Không có URL public: tải qua API có xác thực, hoặc liên kết ký 5 phút (chỉ file không nhạy cảm trên S3).</summary>
[ApiController]
[Route("api/v1")]
[Tags("Attachments")]
[Authorize(Policy = AuthPolicies.OrgMember)]
public sealed class AttachmentsController(ISender sender) : ControllerBase
{
    // Tối đa 20 tệp × 10 MB + phần bao multipart.
    private const long MaxRequestBytes = AttachmentRules.MaxFilesPerRequest * AttachmentRules.MaxFileBytes + 1_048_576;

    [HttpPost("attachments")]
    [Consumes("multipart/form-data")]
    [RequireAntiforgeryToken(false)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [EndpointSummary("Tải lên 1–20 ảnh / PDF (≤ 10 MB mỗi tệp) cho một đối tượng: ownerType, ownerId, category, description?, files[]. Không dùng Idempotency-Key")]
    public async Task<IResult> Upload([FromForm] UploadAttachmentsForm form, CancellationToken ct)
    {
        var files = form.Files.Select(f => new UploadedFile(f.FileName, f.Length, f.OpenReadStream)).ToList();
        var result = await sender.Send(new UploadAttachmentsCommand(form.OwnerType, form.OwnerId, form.Category, form.Description, files), ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/attachments?ownerType={form.OwnerType}&ownerId={form.OwnerId}", result.Value)
            : result.Error!.ToProblem();
    }

    [HttpGet("attachments")]
    [EndpointSummary("Danh sách tệp (chưa xóa) của một đối tượng")]
    public async Task<IResult> List(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken ct) =>
        (await sender.Send(new ListAttachmentsQuery(ownerType, ownerId), ct)).ToHttp();

    [HttpGet("attachments/{id:guid}/content")]
    [NoStore]
    [EndpointSummary("Tải nội dung (stream). Mặc định tải về (attachment); inline=true cho ảnh không nhạy cảm để hiển thị trực tiếp. Tệp nhạy cảm ghi audit mỗi lần tải")]
    public async Task<IResult> Content(Guid id, bool? inline, CancellationToken ct)
    {
        var result = await sender.Send(new GetAttachmentContentQuery(id), ct);
        if (result.IsFailure)
            return result.Error!.ToProblem();

        var file = result.Value!;
        Response.Headers.XContentTypeOptions = "nosniff";
        if (inline == true && !file.IsSensitive && file.ContentType.StartsWith("image/", StringComparison.Ordinal))
        {
            Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
            return Results.Stream(file.Content, file.ContentType);
        }

        return Results.Stream(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("attachments/{id:guid}/signed-url")]
    [NoStore]
    [EndpointSummary("Liên kết tạm 5 phút (chỉ tệp không nhạy cảm trên S3/MinIO; còn lại 422 SIGNED_URL_NOT_SUPPORTED — dùng /content)")]
    public async Task<IResult> SignedUrl(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetAttachmentSignedUrlQuery(id), ct)).ToHttp();

    [HttpDelete("attachments/{id:guid}")]
    [EndpointSummary("Xóa mềm (file vật lý dọn sau 30 ngày); chứng từ thanh toán / ảnh công tơ: 422 ATTACHMENT_LOCKED")]
    public async Task<IResult> Delete(Guid id, CancellationToken ct) =>
        (await sender.Send(new DeleteAttachmentCommand(id), ct)).ToHttp();
}

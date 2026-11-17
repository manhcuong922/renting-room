using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M09 — lưu ảnh / tài liệu đính kèm (Local mặc định; MinIO/S3 chọn bằng cấu hình). Kịch bản: docs/plans/09-file-storage.md §12.</summary>
[Collection(ApiCollection.Name)]
public sealed class FileStorageTests(ApiFactory factory)
{
    private const string Url = "/api/v1/attachments";
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Đủ magic bytes JPEG (FFD8FF) + phần thân giả.</summary>
    private static byte[] Jpeg(int size = 1024)
    {
        var bytes = new byte[size];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00 }.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Pdf() => "%PDF-1.7\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    private static byte[] WindowsExe() => [(byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00];

    private async Task<(string Token, Guid OrgId, Guid RenterId)> OwnerWithRenterAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        return (token, owner.Organization.OrganizationId, await _client.CreateRenterAsync(token));
    }

    // 1. Không cấu hình Storage ⇒ Local; upload CCCD ⇒ 201, nhạy cảm, tải về đúng nội dung.
    [Fact]
    public async Task Upload_IdCardFront_ForRenter_IsSensitive_AndDownloadReturnsSameBytes()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        var content = Jpeg(1024);

        var response = await _client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("cccd.jpg", "image/jpeg", content));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.ReadAsync<JsonElement>()).GetProperty("items")[0];
        created.GetProperty("isSensitive").GetBoolean().Should().BeTrue();
        created.GetProperty("contentType").GetString().Should().Be("image/jpeg");
        created.GetProperty("sizeBytes").GetInt64().Should().Be(1024);
        created.GetProperty("fileName").GetString().Should().Be("cccd.jpg");
        var id = created.GetProperty("id").GetGuid();

        var download = await _client.GetAsync($"{Url}/{id}/content", token);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    // FS-BR-01: tin magic bytes, không tin đuôi / Content-Type.
    [Fact]
    public async Task Upload_ExecutableNamedJpg_IsRejected()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();

        var response = await _client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("anh.jpg", "image/jpeg", WindowsExe()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("FILE_TYPE_NOT_ALLOWED");
    }

    // FS-BR-02
    [Fact]
    public async Task Upload_FileOver10Mb_IsRejected()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();

        var response = await _client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("lon.jpg", "image/jpeg", Jpeg(10 * 1024 * 1024 + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("FILE_TOO_LARGE");
    }

    // FS-BR-03
    [Fact]
    public async Task Upload_CategoryNotAllowedForOwnerType_Returns422()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();

        var response = await _client.UploadAsync(token, "Renter", renterId, "RoomPhoto", ("a.jpg", "image/jpeg", Jpeg()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemCodeAsync()).Should().Be("CATEGORY_NOT_ALLOWED");
    }

    // Hoãn scan hợp đồng (16/11/2026): chưa mở upload cho HĐ.
    [Fact]
    public async Task Upload_ForContract_IsNotSupportedYet()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));

        var response = await _client.UploadAsync(token, "Contract", contractId, "ContractScan", ("hd.pdf", "application/pdf", Pdf()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemCodeAsync()).Should().Be("CATEGORY_NOT_ALLOWED");
    }

    // FS-BR-04 + C-01
    [Fact]
    public async Task OtherOrganization_CannotUploadToOrDownloadFromRenter()
    {
        var (tokenA, _, renterA) = await OwnerWithRenterAsync();
        var tokenB = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;

        var upload = await _client.UploadAsync(tokenB, "Renter", renterA, "IdCardFront", ("a.jpg", "image/jpeg", Jpeg()));
        upload.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await upload.ReadProblemCodeAsync()).Should().Be("OWNER_NOT_FOUND");

        var uploaded = await (await _client.UploadAsync(tokenA, "Renter", renterA, "IdCardFront", ("a.jpg", "image/jpeg", Jpeg()))).ReadAsync<JsonElement>();
        var id = uploaded.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.GetAsync($"{Url}/{id}/content", tokenB)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"{Url}?ownerType=Renter&ownerId={renterA}", tokenB)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Mỗi giấy tờ chỉ giữ 1 ảnh hiệu lực cho mỗi category.
    [Fact]
    public async Task Upload_SecondIdCardFront_ReplacesFirst()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        await _client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("1.jpg", "image/jpeg", Jpeg(100)));
        await _client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("2.jpg", "image/jpeg", Jpeg(200)));
        await _client.UploadAsync(token, "Renter", renterId, "IdCardBack", ("3.jpg", "image/jpeg", Jpeg(300)));

        var list = await (await _client.GetAsync($"{Url}?ownerType=Renter&ownerId={renterId}", token)).ReadAsync<JsonElement>();

        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Single(i => i.GetProperty("category").GetString() == "IdCardFront").GetProperty("fileName").GetString().Should().Be("2.jpg");
    }

    // FS-BR-16
    [Fact]
    public async Task SignedUrl_ForLocalFile_Returns422()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        var uploaded = await (await _client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("a.jpg", "image/jpeg", Jpeg()))).ReadAsync<JsonElement>();
        var id = uploaded.GetProperty("items")[0].GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"{Url}/{id}/signed-url", token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemCodeAsync()).Should().Be("SIGNED_URL_NOT_SUPPORTED");
    }

    // FS-BR-07: tải file nhạy cảm sinh audit; file thường thì không.
    [Fact]
    public async Task Download_SensitiveFile_WritesAudit_ButRoomPhotoDoesNot()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var renterId = await _client.CreateRenterAsync(token);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var idCard = await UploadedIdAsync(token, "Renter", renterId, "IdCardFront");
        var photo = await UploadedIdAsync(token, "Room", roomId, "RoomPhoto");

        (await _client.GetAsync($"{Url}/{idCard}/content", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"{Url}/{photo}/content", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CountAuditAsync("DownloadAttachment", idCard)).Should().Be(1);
        (await CountAuditAsync("DownloadAttachment", photo)).Should().Be(0);
    }

    // Xóa mềm: biến mất khỏi danh sách và không tải được.
    [Fact]
    public async Task Delete_RemovesFromListAndDownload()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        var id = await UploadedIdAsync(token, "Renter", renterId, "Portrait");

        (await _client.DeleteAsync($"{Url}/{id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"{Url}/{id}/content", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await (await _client.GetAsync($"{Url}?ownerType=Renter&ownerId={renterId}", token)).ReadAsync<JsonElement>();
        list.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    // FS-BR-09: ẩn danh người thuê ⇒ xóa file nhạy cảm.
    [Fact]
    public async Task AnonymizeRenter_DeletesSensitiveFiles()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        var id = await UploadedIdAsync(token, "Renter", renterId, "IdCardFront");

        Directory.EnumerateFiles(factory.StorageRoot, "*", SearchOption.AllDirectories)
            .Should().Contain(path => Path.GetFileName(path) == id.ToString("N"), "file có thật trước khi ẩn danh");
        var anonymize = await _client.PostJsonAsync($"/api/v1/renters/{renterId}/anonymize", new { reason = "Người thuê yêu cầu xóa" }, token);
        anonymize.StatusCode.Should().Be(HttpStatusCode.NoContent, await anonymize.Content.ReadAsStringAsync());

        (await _client.GetAsync($"{Url}/{id}/content", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.EnumerateFiles(factory.StorageRoot, "*", SearchOption.AllDirectories)
            .Should().NotContain(path => Path.GetFileName(path) == id.ToString("N"), "file vật lý bị xóa ngay");
    }

    // FS-BR-12 + 14: MinIO không chạy ⇒ app vẫn chạy; upload 503; API khác bình thường; không để lại bản ghi.
    [Fact]
    public async Task S3Configured_ButUnreachable_UploadReturns503_OtherApisWork()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        await using var s3Down = factory.WithWebHostBuilder(UseUnreachableS3);
        var client = s3Down.CreateClient();

        var response = await client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("a.jpg", "image/jpeg", Jpeg()));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.ReadProblemCodeAsync()).Should().Be("STORAGE_UNAVAILABLE");
        (await client.GetAsync($"/api/v1/renters/{renterId}", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await (await client.GetAsync($"{Url}?ownerType=Renter&ownerId={renterId}", token)).ReadAsync<JsonElement>();
        list.GetProperty("items").GetArrayLength().Should().Be(0, "upload lỗi không để lại metadata");
    }

    // FS-BR-15: file đã lưu Local vẫn tải được sau khi đổi cấu hình sang S3.
    [Fact]
    public async Task SwitchingProviderToS3_OldLocalFilesStillDownload()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        var content = Jpeg(777);
        var id = await UploadedIdAsync(token, "Renter", renterId, "IdCardFront", content);

        await using var s3 = factory.WithWebHostBuilder(UseUnreachableS3);
        var download = await s3.CreateClient().GetAsync($"{Url}/{id}/content", token);

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);
    }

    // FS-BR-13: Provider=S3 thiếu khóa ⇒ không khởi động.
    [Fact]
    public void S3Provider_WithoutCredentials_FailsToStart()
    {
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting("Storage:Provider", "S3"));

        var start = () => broken.CreateClient();

        start.Should().Throw<Exception>().Which.ToString().Should().Contain("Storage:S3");
    }

    // FS-BR-02: hạn mức tổ chức, đếm đúng, không vượt khi upload bị từ chối.
    [Fact]
    public async Task Upload_OverOrganizationQuota_Returns507()
    {
        var (token, _, renterId) = await OwnerWithRenterAsync();
        await using var small = factory.WithWebHostBuilder(b => b.UseSetting("Storage:OrganizationQuotaBytes", "3000"));
        var client = small.CreateClient();

        (await client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("1.jpg", "image/jpeg", Jpeg(2000)))).StatusCode.Should().Be(HttpStatusCode.Created);
        var over = await client.UploadAsync(token, "Renter", renterId, "IdCardBack", ("2.jpg", "image/jpeg", Jpeg(2000)));

        over.StatusCode.Should().Be(HttpStatusCode.InsufficientStorage);
        (await over.ReadProblemCodeAsync()).Should().Be("STORAGE_QUOTA_EXCEEDED");
        (await client.UploadAsync(token, "Renter", renterId, "IdCardBack", ("3.jpg", "image/jpeg", Jpeg(900)))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // §3.1: ảnh phòng và PDF chứng từ.
    [Fact]
    public async Task Upload_RoomPhoto_AndPdf_Work_AndInlineIsAllowedOnlyForNonSensitive()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);
        var photo = await UploadedIdAsync(token, "Room", roomId, "RoomPhoto");
        var idCard = await UploadedIdAsync(token, "Renter", renterId, "IdCardFront");

        var inlinePhoto = await _client.GetAsync($"{Url}/{photo}/content?inline=true", token);
        var inlineIdCard = await _client.GetAsync($"{Url}/{idCard}/content?inline=true", token);

        inlinePhoto.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        inlineIdCard.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment", "file nhạy cảm không bao giờ xem nội tuyến");
        (await _client.UploadAsync(token, "Room", roomId, "AssetPhoto", ("x.pdf", "application/pdf", Pdf()))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // §3.1: ảnh công tơ gắn vào chỉ số — tối đa 3 ảnh, không xóa được (chứng cứ).
    [Fact]
    public async Task MeterPhoto_AttachesToReading_MaxThree_AndCannotBeDeleted()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, await _client.FeeIdAsync(token, propertyId, "Điện"), TestData.Today(factory), 1250);
        var readings = await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>();
        var readingId = readings[0].GetProperty("id").GetGuid();

        var first = await UploadedIdAsync(token, "MeterReading", readingId, "MeterPhoto");
        await UploadedIdAsync(token, "MeterReading", readingId, "MeterPhoto");
        await UploadedIdAsync(token, "MeterReading", readingId, "MeterPhoto");
        var fourth = await _client.UploadAsync(token, "MeterReading", readingId, "MeterPhoto", ("4.jpg", "image/jpeg", Jpeg()));

        fourth.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await fourth.ReadProblemCodeAsync()).Should().Be("TOO_MANY_ATTACHMENTS");
        var delete = await _client.DeleteAsync($"{Url}/{first}", token);
        delete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await delete.ReadProblemCodeAsync()).Should().Be("ATTACHMENT_LOCKED");
    }

    // §3.1: chứng từ chuyển khoản gắn vào phiếu thu; PDF được nhận; không xóa được.
    [Fact]
    public async Task PaymentProof_AttachesToPayment_AcceptsPdf_AndCannotBeDeleted()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), month.AddMonths(-1));
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month.AddMonths(-1):yyyy-MM}" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        var invoiceId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await _client.FinalizeInvoiceAsync(invoiceId, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var payment = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments",
            new { amount = 500_000, method = "BankTransfer", paidAt = today, reference = "FT123456", invoiceId }, token);
        payment.StatusCode.Should().Be(HttpStatusCode.Created, await payment.Content.ReadAsStringAsync());
        var paymentId = (await payment.ReadAsync<JsonElement>()).GetProperty("id").GetGuid();

        var created = await _client.UploadAsync(token, "Payment", paymentId, "PaymentProof", ("ck.pdf", "application/pdf", Pdf()));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var item = (await created.ReadAsync<JsonElement>()).GetProperty("items")[0];
        item.GetProperty("contentType").GetString().Should().Be("application/pdf");
        item.GetProperty("isSensitive").GetBoolean().Should().BeFalse();
        var delete = await _client.DeleteAsync($"{Url}/{item.GetProperty("id").GetGuid()}", token);
        (await delete.ReadProblemCodeAsync()).Should().Be("ATTACHMENT_LOCKED");
    }

    private static void UseUnreachableS3(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Provider", "S3");
        builder.UseSetting("Storage:S3:ServiceUrl", "http://127.0.0.1:1");
        builder.UseSetting("Storage:S3:Bucket", "renting-room");
        builder.UseSetting("Storage:S3:AccessKey", "test");
        builder.UseSetting("Storage:S3:SecretKey", "test-secret-key");
        builder.UseSetting("Storage:S3:TimeoutSeconds", "2");
    }

    private async Task<Guid> UploadedIdAsync(string token, string ownerType, Guid ownerId, string category, byte[]? content = null)
    {
        var response = await _client.UploadAsync(token, ownerType, ownerId, category, ("f.jpg", "image/jpeg", content ?? Jpeg()));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private async Task<long> CountAuditAsync(string action, Guid entityId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit_logs WHERE action = @a AND entity_id = @id", connection);
        command.Parameters.AddWithValue("a", action);
        command.Parameters.AddWithValue("id", entityId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

internal static class FileUploadClient
{
    /// <summary>POST /attachments (multipart). Không gửi Idempotency-Key — upload không idempotent.</summary>
    public static Task<HttpResponseMessage> UploadAsync(
        this HttpClient client, string token, string ownerType, Guid ownerId, string category,
        params (string FileName, string ContentType, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(ownerType), "ownerType" },
            { new StringContent(ownerId.ToString()), "ownerId" },
            { new StringContent(category), "category" }
        };
        foreach (var (fileName, contentType, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, "files", fileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/attachments") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}

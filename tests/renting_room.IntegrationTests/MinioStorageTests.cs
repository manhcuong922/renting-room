using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.Minio;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M09 FS-BR-12…16 với MinIO THẬT (container tạm): upload, tải, liên kết ký, xóa, quyền truy cập vào bucket.</summary>
[Collection(ApiCollection.Name)]
public sealed class MinioStorageTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Bucket = "renting-room-test";
    /// <summary>
    /// Image S3 thật cho test. MinIO đã ngừng phát hành image công khai trên Docker Hub / quay.io (kiểm 16/11/2026) ⇒ mặc định dùng bản build
    /// cộng đồng <c>pgsty/minio</c> (cùng mã nguồn MinIO, kéo công khai được, ghim phiên bản để CI ổn định). Đổi image bằng biến <c>MINIO_TEST_IMAGE</c>.
    /// </summary>
    private static readonly string Image = Environment.GetEnvironmentVariable("MINIO_TEST_IMAGE") ?? "pgsty/minio:RELEASE.2026-08-04T00-00-00Z";

    private readonly MinioContainer _minio = new MinioBuilder(Image).Build();

    public Task InitializeAsync() => _minio.StartAsync();

    public Task DisposeAsync() => _minio.DisposeAsync().AsTask();

    private WebApplicationFactory UseMinio() => new(factory, _minio.GetConnectionString(), _minio.GetAccessKey(), _minio.GetSecretKey());

    private sealed class WebApplicationFactory : IAsyncDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _inner;

        public WebApplicationFactory(ApiFactory root, string url, string accessKey, string secretKey) =>
            _inner = root.WithWebHostBuilder(b =>
            {
                b.UseSetting("Storage:Provider", "S3");
                b.UseSetting("Storage:S3:ServiceUrl", url);
                b.UseSetting("Storage:S3:Bucket", Bucket);
                b.UseSetting("Storage:S3:AccessKey", accessKey);
                b.UseSetting("Storage:S3:SecretKey", secretKey);
            });

        public HttpClient CreateClient() => _inner.CreateClient();

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private static byte[] Jpeg(int size)
    {
        var bytes = new byte[size];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00 }.CopyTo(bytes, 0);
        Random.Shared.NextBytes(bytes.AsSpan(16));
        return bytes;
    }

    [Fact]
    public async Task UploadDownloadSignedUrlAndDelete_WorkAgainstRealMinio()
    {
        var setup = factory.CreateClient();
        var owner = await setup.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await setup.CreateRoomAsync(token, await setup.CreatePropertyAsync(token));
        var renterId = await setup.CreateRenterAsync(token);
        await using var s3 = UseMinio();
        var client = s3.CreateClient();

        // Bucket chưa tồn tại ⇒ tự tạo ở lần upload đầu (private).
        var photoBytes = Jpeg(4096);
        var photo = await client.UploadAsync(token, "Room", roomId, "RoomPhoto", ("phong.jpg", "image/jpeg", photoBytes));
        photo.StatusCode.Should().Be(HttpStatusCode.Created, await photo.Content.ReadAsStringAsync());
        var photoId = (await photo.ReadAsync<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();
        var idCard = await client.UploadAsync(token, "Renter", renterId, "IdCardFront", ("cccd.jpg", "image/jpeg", Jpeg(2048)));
        var idCardId = (await idCard.ReadAsync<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();

        var download = await client.GetAsync($"/api/v1/attachments/{photoId}/content", token);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(photoBytes);

        // Liên kết ký: ảnh phòng được; CCCD thì không (FS-BR-16).
        var signed = await (await client.GetAsync($"/api/v1/attachments/{photoId}/signed-url", token)).ReadAsync<JsonElement>();
        using var anonymous = new HttpClient();
        var viaLink = await anonymous.GetAsync(signed.GetProperty("url").GetString());
        viaLink.StatusCode.Should().Be(HttpStatusCode.OK, "liên kết ký không cần đăng nhập");
        (await viaLink.Content.ReadAsByteArrayAsync()).Should().Equal(photoBytes);
        (await client.GetAsync($"/api/v1/attachments/{idCardId}/signed-url", token)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Bucket private: truy cập thẳng không chữ ký bị từ chối (FS-BR-06).
        var direct = await anonymous.GetAsync($"{_minio.GetConnectionString()}/{Bucket}/{signed.GetProperty("url").GetString()!.Split('?')[0].Split('/').Last()}");
        direct.StatusCode.Should().NotBe(HttpStatusCode.OK);

        (await client.DeleteAsync($"/api/v1/attachments/{photoId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/attachments/{photoId}/content", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthReady_ReportsStorageHealthy_WhenMinioIsUp()
    {
        await using var s3 = UseMinio();

        var response = await s3.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Degraded");
    }
}

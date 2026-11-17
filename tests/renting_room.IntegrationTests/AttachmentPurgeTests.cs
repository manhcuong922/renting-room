using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using renting_room.Infrastructure.Jobs;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>M09 FS-11 / FS-BR-08 — job dọn file vật lý của tệp xóa mềm quá hạn. Kịch bản: docs/plans/09-file-storage.md §12 (12–14).</summary>
[Collection(ApiCollection.Name)]
public sealed class AttachmentPurgeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static byte[] Jpeg(int size)
    {
        var bytes = new byte[size];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00 }.CopyTo(bytes, 0);
        return bytes;
    }

    // 12. Xóa mềm quá 30 ngày ⇒ file vật lý biến mất, purged_at có giá trị, used_bytes trả về 0; chạy lại không đổi.
    [Fact]
    public async Task Purge_DeletedMoreThan30DaysAgo_RemovesFileAndReleasesQuota()
    {
        var (token, orgId, id) = await UploadedAndDeletedAsync(2000);
        PhysicalFileExists(id).Should().BeTrue("xóa mềm chưa xóa file vật lý");
        (await UsedBytesAsync(orgId)).Should().Be(2000, "xóa mềm chưa trả hạn mức");
        await BackdateDeletionAsync(id, days: 31);

        var purged = await RunJobAsync(factory, orgId);

        purged.Should().Be(1);
        PhysicalFileExists(id).Should().BeFalse();
        (await PurgedAtAsync(id)).Should().NotBeNull();
        (await UsedBytesAsync(orgId)).Should().Be(0);
        (await RunJobAsync(factory, orgId)).Should().Be(0, "tệp đã dọn không xử lý lại");
        (await UsedBytesAsync(orgId)).Should().Be(0);
        token.Should().NotBeNull();
    }

    // 13. Mới xóa 29 ngày ⇒ không đụng; tệp đang hiệu lực không bao giờ bị dọn.
    [Fact]
    public async Task Purge_DeletedLessThan30DaysAgo_OrStillActive_IsUntouched()
    {
        var (token, orgId, deletedId) = await UploadedAndDeletedAsync(1500);
        PhysicalFileExists(deletedId).Should().BeTrue("xóa mềm chưa xóa file vật lý");
        await BackdateDeletionAsync(deletedId, days: 29);
        var renterId = await _client.CreateRenterAsync(token);
        var activeId = await UploadAsync(token, renterId, 1000);
        await BackdateUploadAsync(activeId, days: 400);

        var purged = await RunJobAsync(factory, orgId);

        purged.Should().Be(0);
        PhysicalFileExists(deletedId).Should().BeTrue();
        PhysicalFileExists(activeId).Should().BeTrue();
        (await PurgedAtAsync(deletedId)).Should().BeNull();
        (await UsedBytesAsync(orgId)).Should().Be(2500);
        (await _client.GetAsync($"/api/v1/attachments/{activeId}/content", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 14. Kho không dùng được ⇒ job không ném lỗi, không đánh dấu, không trả hạn mức.
    [Fact]
    public async Task Purge_WhenStorageUnavailable_SkipsAndKeepsQuota()
    {
        var (_, orgId, id) = await UploadedAndDeletedAsync(1200);
        await BackdateDeletionAsync(id, days: 40);
        await SetProviderAsync(id, "S3");
        await using var s3Down = factory.WithWebHostBuilder(UseUnreachableS3);
        _ = s3Down.CreateClient();

        var purged = await RunJobAsync(s3Down, orgId);

        purged.Should().Be(0);
        (await PurgedAtAsync(id)).Should().BeNull();
        (await UsedBytesAsync(orgId)).Should().Be(1200);
    }

    private static Task<int> RunJobAsync(WebApplicationFactory<Program> app, Guid orgId) =>
        app.Services.GetRequiredService<AttachmentPurgeService>().RunAsync(DateTimeOffset.UtcNow, [orgId], CancellationToken.None);

    private async Task<(string Token, Guid OrgId, Guid AttachmentId)> UploadedAndDeletedAsync(int size)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var renterId = await _client.CreateRenterAsync(token);
        var id = await UploadAsync(token, renterId, size);
        (await _client.DeleteAsync($"/api/v1/attachments/{id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (token, owner.Organization.OrganizationId, id);
    }

    private async Task<Guid> UploadAsync(string token, Guid renterId, int size)
    {
        var response = await _client.UploadAsync(token, "Renter", renterId, "Portrait", ("f.jpg", "image/jpeg", Jpeg(size)));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private bool PhysicalFileExists(Guid id) =>
        Directory.EnumerateFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Any(p => Path.GetFileName(p) == id.ToString("N"));

    private static void UseUnreachableS3(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Provider", "S3");
        builder.UseSetting("Storage:S3:ServiceUrl", "http://127.0.0.1:1");
        builder.UseSetting("Storage:S3:Bucket", "renting-room");
        builder.UseSetting("Storage:S3:AccessKey", "test");
        builder.UseSetting("Storage:S3:SecretKey", "test-secret-key");
        builder.UseSetting("Storage:S3:TimeoutSeconds", "2");
    }

    private Task BackdateDeletionAsync(Guid id, int days) =>
        ExecuteAsync("UPDATE attachments SET deleted_at = now() - make_interval(days => @d) WHERE id = @id", ("d", days), ("id", id));

    private Task BackdateUploadAsync(Guid id, int days) =>
        ExecuteAsync("UPDATE attachments SET uploaded_at = now() - make_interval(days => @d) WHERE id = @id", ("d", days), ("id", id));

    private Task SetProviderAsync(Guid id, string provider) =>
        ExecuteAsync("UPDATE attachments SET storage_provider = @p WHERE id = @id", ("p", provider), ("id", id));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> UsedBytesAsync(Guid orgId) =>
        Convert.ToInt64(await ScalarAsync("SELECT COALESCE((SELECT used_bytes FROM organization_storage_usage WHERE organization_id = @o), 0)", ("o", orgId)));

    private async Task<DateTimeOffset?> PurgedAtAsync(Guid id) =>
        await ScalarAsync("SELECT purged_at FROM attachments WHERE id = @id", ("id", id)) is { } value
            ? new DateTimeOffset(Convert.ToDateTime(value)) : null;

    private async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }
}

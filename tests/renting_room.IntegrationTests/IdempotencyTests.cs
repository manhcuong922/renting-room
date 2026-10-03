using System.Net;
using System.Text;
using Npgsql;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>C-08: gửi lại cùng Idempotency-Key (double-click, retry mạng, gửi song song) không bao giờ thực thi 2 lần.</summary>
[Collection(ApiCollection.Name)]
public sealed class IdempotencyTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task SameKeyTwice_CreatesOnce_AndReplaysOriginalResponse()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerRoomsUrl = await RoomsUrlAsync(owner);
        var key = Guid.NewGuid().ToString();
        var body = Room("P201");

        var first = await _client.PostJsonAsync(ownerRoomsUrl, body, owner.Tokens.AccessToken, key);
        var second = await _client.PostJsonAsync(ownerRoomsUrl, body, owner.Tokens.AccessToken, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse();

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle("true");
        second.Headers.Location.Should().Be(first.Headers.Location);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());

        (await RoomCountAsync(owner)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKey_CreateExactlyOneRoom()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerRoomsUrl = await RoomsUrlAsync(owner);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            factory.CreateClient().PostJsonAsync(ownerRoomsUrl, Room("P301"), owner.Tokens.AccessToken, key)));

        // Một request thực thi; các request khác: 409 (đang xử lý) hoặc 201 replay (nếu đến sau khi xong).
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created && !r.Headers.Contains("Idempotent-Replayed")).Should().Be(1);
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            (await conflict.ReadProblemCodeAsync()).Should().Be("IDEMPOTENCY_REQUEST_IN_PROGRESS");

        (await RoomCountAsync(owner)).Should().Be(1);
    }

    [Fact]
    public async Task SameKeyWithDifferentBody_Returns422()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerRoomsUrl = await RoomsUrlAsync(owner);
        var key = Guid.NewGuid().ToString();
        await _client.PostJsonAsync(ownerRoomsUrl, Room("P401"), owner.Tokens.AccessToken, key);

        var reused = await _client.PostJsonAsync(ownerRoomsUrl, Room("P402"), owner.Tokens.AccessToken, key);

        reused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await reused.ReadProblemCodeAsync()).Should().Be("IDEMPOTENCY_KEY_REUSED");
        (await RoomCountAsync(owner)).Should().Be(1);
    }

    [Fact]
    public async Task MissingKey_OnRequiredEndpoint_Returns400_AndCreatesNothing()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerRoomsUrl = await RoomsUrlAsync(owner);

        var response = await _client.PostJsonAsync(ownerRoomsUrl, Room("P501"), owner.Tokens.AccessToken, idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        (await RoomCountAsync(owner)).Should().Be(0);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces in key")]
    [InlineData("ký-tự-có-dấu-123")]
    public async Task InvalidKeyFormat_Returns400(string key)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerRoomsUrl = await RoomsUrlAsync(owner);

        var response = await _client.PostJsonAsync(ownerRoomsUrl, Room("P601"), owner.Tokens.AccessToken, key);

        (await response.ReadProblemCodeAsync()).Should().Be("INVALID_IDEMPOTENCY_KEY");
    }

    [Fact]
    public async Task FailedRequest_ReleasesKey_SoClientCanRetryWithSameKey()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerRoomsUrl = await RoomsUrlAsync(owner);
        var key = Guid.NewGuid().ToString();

        var invalid = await _client.PostJsonAsync(ownerRoomsUrl, new { code = "", spec = new { maxOccupants = 0 } }, owner.Tokens.AccessToken, key);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var corrected = await _client.PostJsonAsync(ownerRoomsUrl, Room("P701"), owner.Tokens.AccessToken, key);
        corrected.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task SameKey_ForDifferentUsers_IsIndependent()
    {
        var ownerA = await _client.CreateActiveOwnerAsync();
        var ownerARoomsUrl = await RoomsUrlAsync(ownerA);
        var ownerB = await _client.CreateActiveOwnerAsync();
        var ownerBRoomsUrl = await RoomsUrlAsync(ownerB);
        var key = Guid.NewGuid().ToString();

        var a = await _client.PostJsonAsync(ownerARoomsUrl, Room("A1"), ownerA.Tokens.AccessToken, key);
        var b = await _client.PostJsonAsync(ownerBRoomsUrl, Room("B1"), ownerB.Tokens.AccessToken, key);

        a.StatusCode.Should().Be(HttpStatusCode.Created);
        b.StatusCode.Should().Be(HttpStatusCode.Created);
        b.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task CreateOrganization_Retry_ReturnsSameTemporaryPassword_StoredEncrypted()
    {
        var admin = await _client.LoginAdminAsync();
        var key = Guid.NewGuid().ToString();
        var body = new
        {
            code = TestApi.NewOrganizationCode(),
            name = "Retry mạng",
            owner = new { fullName = "Chủ trọ", phone = TestApi.NewPhone() }
        };

        var first = await _client.PostJsonAsync("/api/v1/admin/organizations", body, admin.AccessToken, key);
        var retry = await _client.PostJsonAsync("/api/v1/admin/organizations", body, admin.AccessToken, key);

        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        var original = await first.ReadAsync<CreatedOrganization>();
        var replayed = await retry.ReadAsync<CreatedOrganization>();
        replayed.Should().Be(original);

        // Response chứa mật khẩu tạm được lưu MÃ HÓA, không phải dạng rõ.
        var storedBody = await ReadStoredResponseAsync(key);
        storedBody.Should().NotBeNull();
        Encoding.UTF8.GetString(storedBody!).Should().NotContain(original.TemporaryPassword);
    }

    private static object Room(string code) => new { code, spec = new { maxOccupants = 2, listedRent = 3_000_000 } };

    private async Task<string> RoomsUrlAsync(OwnerAccount owner) =>
        $"/api/v1/properties/{await _client.CreatePropertyAsync(owner.Tokens.AccessToken, withLessor: false)}/rooms";

    private async Task<int> RoomCountAsync(OwnerAccount owner)
    {
        var rooms = await _client.GetAsync("/api/v1/rooms", owner.Tokens.AccessToken);
        return (await rooms.ReadAsync<Page<object>>()).TotalCount;
    }

    private async Task<byte[]?> ReadStoredResponseAsync(string key)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT response_body FROM idempotency_keys WHERE key = @key", connection);
        command.Parameters.AddWithValue("key", key);
        return await command.ExecuteScalarAsync() as byte[];
    }
}

using System.Net;
using System.Text.Json;
using Npgsql;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// C-10: thay đổi entity ghi audit cùng transaction với SaveChanges; thao tác đọc dữ liệu nhạy cảm ghi audit ở nền theo lô.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditLogTests(ApiFactory factory)
{
    private sealed record AuditRow(string Action, string EntityType, Guid? UserId, Guid? OrganizationId, string? Changes, string? IpAddress);

    private static readonly TimeSpan BackgroundWriteTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _client = factory.CreateClient();

    private static object RenterInput(string idNumber, string phone) => new
    {
        fullName = "Trần Thị Lan",
        dateOfBirth = "2000-04-15",
        gender = "Female",
        phone,
        nationality = "VN",
        idType = "CitizenId",
        idNumber,
        permanentAddress = "Nam Định"
    };

    [Fact]
    public async Task RenterCreateAndUpdate_AreAudited_WithIdNumberRedacted()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var oldIdNumber = TestData.NewCitizenId();
        var newIdNumber = TestData.NewCitizenId();
        var renterId = await _client.CreateRenterAsync(token, oldIdNumber, phone: "0911111111");

        var updated = await _client.PutJsonAsync($"/api/v1/renters/{renterId}",
            new { renter = RenterInput(newIdNumber, "0922222222"), version = await RenterVersionAsync(renterId, token) }, token);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        var logs = await ReadAuditLogsAsync(renterId);
        logs.Select(l => l.Action).Should().BeEquivalentTo(["Created", "Updated"]);
        logs.Should().AllSatisfy(l =>
        {
            l.EntityType.Should().Be("Renter");
            l.UserId.Should().Be(owner.Organization.OwnerUserId);
            l.OrganizationId.Should().Be(owner.Organization.OrganizationId);
            l.Changes.Should().NotContain(oldIdNumber).And.NotContain(newIdNumber, "số giấy tờ không bao giờ nằm trong audit");
        });

        using var changes = JsonDocument.Parse(logs.Single(l => l.Action == "Updated").Changes!);
        var phone = changes.RootElement.GetProperty("phone");
        phone.GetProperty("old").GetString().Should().Be("0911111111");
        phone.GetProperty("new").GetString().Should().Be("0922222222");
        changes.RootElement.GetProperty("idNumberEncrypted").GetString().Should().Be("[redacted]");
        changes.RootElement.TryGetProperty("fullName", out _).Should().BeFalse("chỉ ghi trường có thay đổi");
    }

    [Fact]
    public async Task FailedSave_LeavesNoAuditRow()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var idNumber = TestData.NewCitizenId();
        var renterId = await _client.CreateRenterAsync(token, idNumber);
        var staleVersion = await RenterVersionAsync(renterId, token);
        (await _client.PutJsonAsync($"/api/v1/renters/{renterId}",
            new { renter = RenterInput(idNumber, "0933333333"), version = staleVersion }, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var conflict = await _client.PutJsonAsync($"/api/v1/renters/{renterId}",
            new { renter = RenterInput(idNumber, "0944444444"), version = staleVersion }, token);

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAuditLogsAsync(renterId)).Count(l => l.Action == "Updated").Should().Be(1, "lần lưu bị rollback không để lại audit");
    }

    [Fact]
    public async Task RevealIdNumber_IsAuditedInBackground_WithClientIp()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var renterId = await _client.CreateRenterAsync(token);

        var revealed = await _client.PostJsonAsync($"/api/v1/renters/{renterId}/reveal-id-number", null, token, clientIp: "203.0.113.7");
        revealed.StatusCode.Should().Be(HttpStatusCode.OK);

        var reveal = await WaitForAuditLogAsync(renterId, "RevealIdNumber");
        reveal.EntityType.Should().Be("Renter");
        reveal.UserId.Should().Be(owner.Organization.OwnerUserId);
        reveal.OrganizationId.Should().Be(owner.Organization.OrganizationId);
        reveal.IpAddress.Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task Owner_ReadsEntityHistory_WithUserName_OtherOrganizationSeesNothing()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var renterId = await _client.CreateRenterAsync(token, phone: "0911111111");
        // Đồng hồ giả đứng yên ⇒ 2 dòng cùng OccurredAt, phần ngẫu nhiên của UUIDv7 làm thứ tự "mới nhất trước" không xác định.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await _client.PutJsonAsync($"/api/v1/renters/{renterId}",
            new { renter = RenterInput(TestData.NewCitizenId(), "0922222222"), version = await RenterVersionAsync(renterId, token) }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await (await _client.GetAsync($"/api/v1/audit-logs?entityType=Renter&entityId={renterId}", token)).ReadAsync<JsonElement>();

        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("action").GetString()).Should().Equal("Updated", "Created"); // mới nhất trước
        items.Should().AllSatisfy(i =>
        {
            i.GetProperty("userId").GetGuid().Should().Be(owner.Organization.OwnerUserId);
            i.GetProperty("userName").GetString().Should().NotBeNullOrEmpty();
        });
        items[0].GetProperty("changes").GetProperty("phone").GetProperty("new").GetString().Should().Be("0922222222");

        var stranger = await _client.CreateActiveOwnerAsync();
        var foreign = await (await _client.GetAsync($"/api/v1/audit-logs?entityId={renterId}", stranger.Tokens.AccessToken)).ReadAsync<JsonElement>();
        foreign.GetProperty("totalCount").GetInt32().Should().Be(0, "nhật ký không lộ sang tổ chức khác (C-01)");
    }

    [Fact]
    public async Task Manager_CannotReadAuditLogs()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var created = await _client.PostJsonAsync("/api/v1/org/members",
            new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, owner.Tokens.AccessToken);
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        var manager = await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "PhoQuanLy2026" }, login.AccessToken)).ReadAsync<TokenResponse>();

        (await _client.GetAsync("/api/v1/audit-logs", manager.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<uint> RenterVersionAsync(Guid renterId, string token)
    {
        var renter = await (await _client.GetAsync($"/api/v1/renters/{renterId}", token)).ReadAsync<JsonElement>();
        return uint.Parse(renter.GetProperty("version").GetString()!);
    }

    private async Task<AuditRow> WaitForAuditLogAsync(Guid entityId, string action)
    {
        var deadline = DateTime.UtcNow + BackgroundWriteTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var row = (await ReadAuditLogsAsync(entityId)).FirstOrDefault(l => l.Action == action);
            if (row is not null)
                return row;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Audit '{action}' for {entityId} was not written within {BackgroundWriteTimeout}.");
    }

    private async Task<List<AuditRow>> ReadAuditLogsAsync(Guid entityId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT action, entity_type, user_id, organization_id, changes::text, ip_address FROM audit_logs WHERE entity_id = @id",
            connection);
        command.Parameters.AddWithValue("id", entityId);

        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }
}

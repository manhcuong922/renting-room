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

    /// <summary>
    /// L-16 (05/11/2026, README C-10, LEG-06): nhật ký xem số giấy tờ ghi thẳng TRƯỚC khi trả số — chưa ghi được thì chưa trả
    /// (trước đây qua hàng đợi RAM, crash mất tới 2 giây nhật ký).
    /// </summary>
    [Fact]
    public async Task RevealIdNumber_WritesAuditBeforeReturningNumber_WithClientIp()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var renterId = await _client.CreateRenterAsync(token);

        // Kết nối khác chặn ghi vào audit_logs ⇒ lệnh xem phải chờ, không được trả số trước khi có nhật ký.
        await using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var holder = await connection.BeginTransactionAsync();
        await using (var lockTable = new Npgsql.NpgsqlCommand("LOCK TABLE audit_logs IN EXCLUSIVE MODE", connection, holder))
            await lockTable.ExecuteNonQueryAsync();

        var request = factory.CreateClient().PostJsonAsync($"/api/v1/renters/{renterId}/reveal-id-number", null, token, clientIp: "203.0.113.7");
        (await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(1)))).Should().NotBeSameAs(request, "chưa ghi được nhật ký ⇒ chưa trả số");
        await holder.RollbackAsync();

        var revealed = await request;
        revealed.StatusCode.Should().Be(HttpStatusCode.OK);
        var reveal = (await ReadAuditLogsAsync(renterId)).Single(l => l.Action == "RevealIdNumber"); // có ngay, không chờ ghi nền
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

    /// <summary>
    /// M-13 (07/11/2026): tìm theo số giấy tờ và tạo hồ sơ trùng số (409 lộ họ tên) là 2 đường dò CCCD ⇒ ghi audit "IdNumberLookup"
    /// (ai, khi nào, kết quả) — KHÔNG ghi số đã nhập.
    /// </summary>
    [Fact]
    public async Task IdNumberLookup_SearchAndDuplicateCreate_AreAudited_WithoutTheNumber()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var existing = await _client.CreateRenterAsync(token, idNumber: "079200004321");

        (await _client.PostJsonAsync("/api/v1/renters/search", new { idNumber = "079200004321" }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync("/api/v1/renters",
            new { fullName = "Người Trùng", dateOfBirth = "2000-01-01", gender = "Male", idType = "CitizenId", idNumber = "079200004321" }, token))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var rows = await LookupAuditAsync(owner.Organization.OrganizationId);
        rows.Should().HaveCount(2);
        var details = rows.Select(r => JsonDocument.Parse(r.Changes!).RootElement).ToList();
        details.Should().Contain(d => d.GetProperty("source").GetString() == "Search" && d.GetProperty("matches").GetInt32() == 1);
        rows.Should().Contain(r => r.EntityId == existing && JsonDocument.Parse(r.Changes!).RootElement.GetProperty("source").GetString() == "Create");
        rows.Should().AllSatisfy(r => r.Changes.Should().NotContain("079200004321", "số giấy tờ không bao giờ nằm trong audit"));

        // Tìm theo tên không phải dò số giấy tờ ⇒ không ghi.
        await _client.PostJsonAsync("/api/v1/renters/search", new { q = "Trần" }, token);
        (await LookupAuditAsync(owner.Organization.OrganizationId)).Should().HaveCount(2);
    }

    /// <summary>M-15 (07/11/2026, RP-BR-03): xuất danh sách người thuê KHÔNG kèm số giấy tờ vẫn chứa ngày sinh / SĐT / địa chỉ ⇒ cũng ghi audit.</summary>
    [Fact]
    public async Task ExportRenters_WithoutSensitive_IsAuditedWithRowCount()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        await _client.CreateRenterAsync(token);
        var (_, _, _, _) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));

        (await _client.PostJsonAsync("/api/v1/exports/renters", new { includeSensitive = false }, token, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT user_id, changes::text FROM audit_logs WHERE action = 'ExportRenters' AND organization_id = @org", connection);
        command.Parameters.AddWithValue("org", owner.Organization.OrganizationId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("export nào cũng để lại dấu vết");
        reader.GetGuid(0).Should().Be(owner.Organization.OwnerUserId);
        reader.GetString(1).Should().Contain("rowCount");
        (await reader.ReadAsync()).Should().BeFalse();
    }

    private async Task<List<(Guid? EntityId, string? Changes)>> LookupAuditAsync(Guid organizationId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT entity_id, changes::text FROM audit_logs WHERE action = 'IdNumberLookup' AND organization_id = @org", connection);
        command.Parameters.AddWithValue("org", organizationId);
        var rows = new List<(Guid?, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return rows;
    }

    private async Task<uint> RenterVersionAsync(Guid renterId, string token)
    {
        var renter = await (await _client.GetAsync($"/api/v1/renters/{renterId}", token)).ReadAsync<JsonElement>();
        return uint.Parse(renter.GetProperty("version").GetString()!);
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

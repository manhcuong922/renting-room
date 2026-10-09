using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using renting_room.Infrastructure.Jobs;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>RT-BR-06: job hằng ngày ẩn danh người thuê rời đi quá 36 tháng — xóa dữ liệu cá nhân ở hồ sơ và trong audit log.</summary>
[Collection(ApiCollection.Name)]
public sealed class RenterAnonymizationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private RenterAnonymizationService Job => factory.Services.GetRequiredService<RenterAnonymizationService>();

    [Fact]
    public async Task RenterInactiveOver36Months_IsAnonymized_ActiveContractRenterIsKept()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var organization = owner.Organization.OrganizationId;
        var today = TestData.Today(factory);
        var idle = await _client.CreateRenterAsync(token, phone: "0911111111");
        var (_, _, tenant, _) = await _client.CreateActiveContractAsync(token, today);

        (await Job.RunAsync(today.AddMonths(35), [organization], CancellationToken.None)).Should().Be(0, "chưa đủ 36 tháng");
        (await Job.RunAsync(today.AddMonths(37), [organization], CancellationToken.None)).Should().Be(1, "người đang ở HĐ hiệu lực không bị ẩn danh");

        var renter = await (await _client.GetAsync($"/api/v1/renters/{idle}", token)).ReadAsync<JsonElement>();
        renter.GetProperty("fullName").GetString().Should().StartWith("Đã ẩn danh #");
        renter.GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
        renter.GetProperty("idNumberMasked").ValueKind.Should().Be(JsonValueKind.Null, "giấy tờ bị xóa hẳn");
        renter.GetProperty("anonymizedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        var kept = await (await _client.GetAsync($"/api/v1/renters/{tenant}", token)).ReadAsync<JsonElement>();
        kept.GetProperty("anonymizedAt").ValueKind.Should().Be(JsonValueKind.Null);

        (await (await _client.PostJsonAsync($"/api/v1/renters/{idle}/reveal-id-number", null, token)).ReadProblemCodeAsync())
            .Should().Be("RENTER_ANONYMIZED");
        (await Job.RunAsync(today.AddMonths(40), [organization], CancellationToken.None)).Should().Be(0, "đã ẩn danh thì bỏ qua");

        var audit = await AuditChangesAsync(idle);
        audit.Should().Contain(a => a.Action == "Anonymized");
        audit.Where(a => a.Action != "Anonymized").Should().OnlyContain(a => a.Changes == null, "giá trị cá nhân bị xóa khỏi nhật ký");
    }

    [Fact]
    public async Task OrganizationSettings_LongerRetentionOrDisabled_JobSkips()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var organization = owner.Organization.OrganizationId;
        var today = TestData.Today(factory);
        await _client.CreateRenterAsync(token);

        (await _client.PutJsonAsync("/api/v1/org/data-retention", new { retentionMonths = 30, autoAnonymize = true }, token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "tối thiểu 36 tháng");
        (await _client.PutJsonAsync("/api/v1/org/data-retention", new { retentionMonths = 60, autoAnonymize = true }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Job.RunAsync(today.AddMonths(37), [organization], CancellationToken.None)).Should().Be(0, "tổ chức giữ 60 tháng");

        var disabled = await (await _client.PutJsonAsync("/api/v1/org/data-retention", new { retentionMonths = 60, autoAnonymize = false }, token))
            .ReadAsync<JsonElement>();
        disabled.GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("AUTO_ANONYMIZE_DISABLED");
        (await Job.RunAsync(today.AddMonths(70), [organization], CancellationToken.None)).Should().Be(0, "đã tắt ẩn danh tự động");
    }

    [Fact]
    public async Task ManualAnonymize_OwnerOnly_ReasonRequired_BlockedWhileContractOpen()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var idle = await _client.CreateRenterAsync(token);
        var (_, _, tenant, _) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));

        var created = await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, token);
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        var manager = await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "PhoQuanLy2026" }, login.AccessToken)).ReadAsync<TokenResponse>();
        (await _client.PostJsonAsync($"/api/v1/renters/{idle}/anonymize", new { reason = "Yêu cầu xóa dữ liệu" }, manager.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await _client.PostJsonAsync($"/api/v1/renters/{idle}/anonymize", new { reason = "" }, token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await (await _client.PostJsonAsync($"/api/v1/renters/{tenant}/anonymize", new { reason = "Thử" }, token)).ReadProblemCodeAsync())
            .Should().Be("RENTER_HAS_ACTIVE_CONTRACT");

        (await _client.PostJsonAsync($"/api/v1/renters/{idle}/anonymize", new { reason = "Người thuê yêu cầu xóa dữ liệu" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var renter = await (await _client.GetAsync($"/api/v1/renters/{idle}", token)).ReadAsync<JsonElement>();
        renter.GetProperty("fullName").GetString().Should().StartWith("Đã ẩn danh #");
        (await (await _client.PostJsonAsync($"/api/v1/renters/{idle}/anonymize", new { reason = "Lần 2" }, token)).ReadProblemCodeAsync())
            .Should().Be("RENTER_ANONYMIZED");
    }

    [Fact]
    public async Task Anonymize_ErasesPlatesOfEndedVehicles_InDataAndAuditLog()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var (_, _, tenant, contractId) = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles",
                new { renterId = tenant, vehicleType = "Motorbike", plateNumber = "29B1-555.66", brandColor = "Wave đỏ" }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today, reason = "MutualAgreement" }, token);
        await _client.SettleAndCompleteAsync(token, contractId);

        (await _client.PostJsonAsync($"/api/v1/renters/{tenant}/anonymize", new { reason = "Người thuê yêu cầu xóa dữ liệu" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var vehicles = new NpgsqlCommand(
            "SELECT count(*) FROM contract_vehicles WHERE contract_id = @id AND (plate_number IS NOT NULL OR brand_color IS NOT NULL)", connection);
        vehicles.Parameters.AddWithValue("id", contractId);
        ((long)(await vehicles.ExecuteScalarAsync())!).Should().Be(0, "biển số nhận diện được người (RT-BR-06)");
        await using var audit = new NpgsqlCommand(
            "SELECT count(*) FROM audit_logs WHERE entity_type = 'ContractVehicle' AND changes::text LIKE '%29B155566%'", connection);
        ((long)(await audit.ExecuteScalarAsync())!).Should().Be(0);
    }

    private async Task<List<(string Action, string? Changes)>> AuditChangesAsync(Guid renterId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT action, changes::text FROM audit_logs WHERE entity_type = 'Renter' AND entity_id = @id", connection);
        command.Parameters.AddWithValue("id", renterId);
        var rows = new List<(string, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return rows;
    }
}

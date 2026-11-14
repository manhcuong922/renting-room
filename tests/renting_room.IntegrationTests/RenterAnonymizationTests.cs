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
        renter.GetProperty("fullName").GetString().Should().Be("Trần Thị Lan", "hết thời gian giữ: giữ họ tên để tra lịch sử phòng, xóa phần còn lại");
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
    public async Task OneBrokenRecord_DoesNotBlockTheRestOfTheRun()
    {
        // M-26 (07/11/2026): hồ sơ có giấy tờ mã hóa hỏng (mất khóa) làm Decrypt ném lỗi. Danh sách xếp theo ngày rời đi tăng dần ⇒
        // trước đây ngày nào job cũng dừng ở đúng hồ sơ đó, mọi hồ sơ phía sau không bao giờ được ẩn danh.
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var organization = owner.Organization.OrganizationId;
        var today = TestData.Today(factory);
        var broken = await _client.CreateRenterAsync(token, phone: "0911111112");
        var healthy = await _client.CreateRenterAsync(token, phone: "0911111113");
        await CorruptIdNumberAsync(broken);
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync();
            // Hồ sơ hỏng "rời đi" sớm hơn ⇒ đứng đầu danh sách.
            await using var older = new NpgsqlCommand("UPDATE renters SET created_at = created_at - interval '1 day' WHERE id = @id", connection);
            older.Parameters.AddWithValue("id", broken);
            await older.ExecuteNonQueryAsync();
        }

        var anonymized = await Job.RunAsync(today.AddMonths(40), [organization], CancellationToken.None);

        anonymized.Should().Be(1, "chỉ đếm hồ sơ ẩn danh thành công");
        (await GetAnonymizedAtAsync(token, healthy)).ValueKind.Should().NotBe(JsonValueKind.Null, "hồ sơ phía sau vẫn được xử lý");
        (await GetAnonymizedAtAsync(token, broken)).ValueKind.Should().Be(JsonValueKind.Null, "hồ sơ lỗi giữ nguyên để người vận hành xử lý");
    }

    [Fact]
    public async Task Anonymize_RechecksOpenContractInsideTransaction_StaleDecisionDoesNotEraseARenterWhoCameBack()
    {
        // M-23 (07/11/2026): job / lệnh tay kiểm "còn HĐ mở" NGOÀI transaction rồi mới xóa ⇒ tạo HĐ cho người quay lại đúng lúc đó
        // thì hồ sơ vẫn bị xóa giấy tờ, không sửa lại được. Mô phỏng quyết định đã cũ: gọi thẳng bộ ẩn danh cho người vừa có HĐ nháp.
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var renter = await _client.CreateRenterAsync(token, phone: "0911111114");
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        await _client.CreateContractAsync(token, roomId, renter, today);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(new OrganizationSystemUser(owner.Organization.OrganizationId));
        var result = await scope.ServiceProvider.GetRequiredService<renting_room.Application.Renters.RenterAnonymizer>()
            .AnonymizeAsync(renter, reason: null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RENTER_HAS_ACTIVE_CONTRACT");
        (await GetAnonymizedAtAsync(token, renter)).ValueKind.Should().Be(JsonValueKind.Null, "hồ sơ còn nguyên");
    }

    private async Task<JsonElement> GetAnonymizedAtAsync(string token, Guid renterId) =>
        (await (await _client.GetAsync($"/api/v1/renters/{renterId}", token)).ReadAsync<JsonElement>()).GetProperty("anonymizedAt");

    private async Task CorruptIdNumberAsync(Guid renterId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE renters SET id_number_encrypted = @garbage WHERE id = @id", connection);
        command.Parameters.AddWithValue("garbage", new byte[] { 1, 2, 3, 4, 5 });
        command.Parameters.AddWithValue("id", renterId);
        await command.ExecuteNonQueryAsync();
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

    private const string Phone = "0901234567";
    private const string Erased = "(đã xóa khi ẩn danh)";

    /// <summary>HĐ đã kết thúc của "Trần Thị Lan" (SĐT 0901234567) với ghi chú HĐ và ghi chú thanh lý nhắc tên, SĐT, số CCCD.</summary>
    private async Task<(string Token, Guid Organization, Guid RenterId, Guid ContractId, string IdNumber)> EndedContractWithNotesAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 10);
        var idNumber = TestData.NewCitizenId();
        var renterId = await _client.CreateRenterAsync(token, idNumber: idNumber, phone: Phone);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day);
        var contractId = await _client.CreateContractAsync(token, await _client.CreateRoomAsync(token, propertyId), renterId, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/note", new { note = "Chị Trần Thị Lan hẹn trả tiền ngày 10, gọi 0901.234.567" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
                new { actualEndDate = today, reason = "MutualAgreement", note = $"Trần Thị Lan (+84 901 234 567, CCCD {idNumber}) trả phòng, trừ cọc vì vỡ kính" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _client.SettleAndCompleteAsync(token, contractId);
        return (token, owner.Organization.OrganizationId, renterId, contractId, idNumber);
    }

    private async Task<(string? Note, string? TerminationNote, string? AuditText)> NotesAsync(Guid contractId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var notes = new NpgsqlCommand("SELECT note, termination_note FROM contracts WHERE id = @id", connection);
        notes.Parameters.AddWithValue("id", contractId);
        string? note, termination;
        await using (var reader = await notes.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            note = reader.IsDBNull(0) ? null : reader.GetString(0);
            termination = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        await using var audit = new NpgsqlCommand(
            "SELECT string_agg(changes::text, ' ') FROM audit_logs WHERE entity_type = 'Contract' AND entity_id = @id", connection);
        audit.Parameters.AddWithValue("id", contractId);
        var auditText = await audit.ExecuteScalarAsync() as string;
        return (note, termination, auditText);
    }

    /// <summary>
    /// L-17 (05/11/2026, RT-BR-06): ẩn danh bằng tay ⇒ che họ tên / SĐT (mọi cách viết số) / số giấy tờ của chính người đó trong ghi chú,
    /// giữ lý do nghiệp vụ; nhật ký bỏ hẳn trường ghi chú.
    /// </summary>
    [Fact]
    public async Task ManualAnonymize_MasksOwnIdentifiersInNotes_KeepsBusinessReason_DropsNotesFromAudit()
    {
        var s = await EndedContractWithNotesAsync();

        (await _client.PostJsonAsync($"/api/v1/renters/{s.RenterId}/anonymize", new { reason = "Người thuê yêu cầu xóa dữ liệu" }, s.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (note, termination, audit) = await NotesAsync(s.ContractId);
        note.Should().Be("Chị *** hẹn trả tiền ngày 10, gọi ***");
        termination.Should().Be("*** (***, CCCD ***) trả phòng, trừ cọc vì vỡ kính");
        (audit ?? "").Should().NotContain("901").And.NotContain(s.IdNumber).And.NotContain("Trần Thị Lan");
    }

    /// <summary>L-17: ẩn danh tự động (≥ 36 tháng, quá thời hiệu) ⇒ xóa trắng ghi chú HĐ người đó đứng tên.</summary>
    [Fact]
    public async Task AutomaticAnonymize_ClearsNotesOfOwnContracts()
    {
        var s = await EndedContractWithNotesAsync();

        (await Job.RunAsync(TestData.Today(factory).AddMonths(37), [s.Organization], CancellationToken.None)).Should().Be(1);

        var (note, termination, audit) = await NotesAsync(s.ContractId);
        note.Should().Be(Erased);
        termination.Should().Be(Erased);
        (audit ?? "").Should().NotContain("901").And.NotContain(s.IdNumber).And.NotContain("hẹn trả tiền");
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

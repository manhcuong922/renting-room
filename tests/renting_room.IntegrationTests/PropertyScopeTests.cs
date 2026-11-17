using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M01 ID-BR-24..29 (08/11/2026): "phó quản lý" thành <b>quản lý khu</b> — chủ trọ giao khu cho từng người; quản lý làm mọi việc như chủ nhưng chỉ
/// trong khu được giao (dữ liệu khu khác coi như không tồn tại), trừ danh sách chỉ chủ trọ (ngân hàng, bên cho thuê, tạo / ngừng khu, mẫu HĐ, thành viên…).
/// Kịch bản S1–S8 ở plan 01 §12.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PropertyScopeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Site(Guid PropertyId, Guid RoomId, Guid RenterId, Guid ContractId, string IdNumber);

    /// <summary>Khu + phòng + người thuê (CCCD biết trước) + HĐ đã kích hoạt.</summary>
    private async Task<Site> SiteAsync(string token)
    {
        var start = TestData.StartWithinOnePeriod(TestData.Today(factory), 3);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var idNumber = TestData.NewCitizenId();
        var renterId = await _client.CreateRenterAsync(token, idNumber);
        var contractId = await _client.CreateContractAsync(token, roomId, renterId, start);
        var activated = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());
        return new Site(propertyId, roomId, renterId, contractId, idNumber);
    }

    private async Task<(Guid UserId, string Token)> ManagerAsync(string ownerToken, params Guid[] propertyIds)
    {
        var created = await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Quản Lý Khu", phone = TestApi.NewPhone(), propertyIds }, ownerToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        var tokens = await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "QuanLyKhu2026" }, login.AccessToken)).ReadAsync<TokenResponse>();
        return (credentials.UserId, tokens.AccessToken);
    }

    private async Task<List<Guid>> IdsAsync(string url, string token)
    {
        var response = await _client.GetAsync(url, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadAsync<JsonElement>();
        var items = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items");
        return items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    private Task<HttpStatusCode> StatusAsync(string url, string token) => _client.GetAsync(url, token).ContinueWith(t => t.Result.StatusCode);

    [Fact]
    public async Task S1_S2_S7_Manager_SeesAndChangesOnlyAssignedProperty_OwnerSeesAll()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerToken = owner.Tokens.AccessToken;
        var a = await SiteAsync(ownerToken);
        var b = await SiteAsync(ownerToken);
        var (_, token) = await ManagerAsync(ownerToken, a.PropertyId);

        // Đọc: danh sách chỉ có khu A; mở dữ liệu khu B = như id sai.
        (await IdsAsync("/api/v1/properties", token)).Should().Equal(a.PropertyId);
        (await IdsAsync("/api/v1/contracts", token)).Should().Contain(a.ContractId).And.NotContain(b.ContractId);
        (await IdsAsync("/api/v1/rooms", token)).Should().Contain(a.RoomId).And.NotContain(b.RoomId);
        (await IdsAsync("/api/v1/renters", token)).Should().Contain(a.RenterId).And.NotContain(b.RenterId);
        (await StatusAsync($"/api/v1/properties/{a.PropertyId}", token)).Should().Be(HttpStatusCode.OK);
        foreach (var url in new[]
                 {
                     $"/api/v1/properties/{b.PropertyId}", $"/api/v1/rooms/{b.RoomId}", $"/api/v1/contracts/{b.ContractId}", $"/api/v1/renters/{b.RenterId}",
                     $"/api/v1/properties/{b.PropertyId}/fee-types", $"/api/v1/contracts/{b.ContractId}/balance"
                 })
            (await StatusAsync(url, token)).Should().Be(HttpStatusCode.NotFound, url);

        // Ghi vào khu B: không tìm thấy, dữ liệu B không đổi.
        (await _client.PostJsonAsync($"/api/v1/properties/{b.PropertyId}/rooms",
                new { code = "X01", spec = new { maxOccupants = 2, listedRent = 3_000_000 } }, token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostJsonAsync($"/api/v1/contracts/{b.ContractId}/payments",
                new { amount = 100_000, method = "Cash", paidAt = TestData.Today(factory) }, token, idempotencyKey: Guid.NewGuid().ToString()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var month = $"{TestData.Today(factory):yyyy-MM}";
        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId = b.PropertyId, billingMonth = month }, token,
                idempotencyKey: Guid.NewGuid().ToString()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Tạo phiếu "mọi khu" ⇒ chỉ khu được giao.
        var bulk = await (await _client.PostJsonAsync("/api/v1/invoices/generate-bulk", new { billingMonth = month, propertyIds = (Guid[]?)null }, token,
            idempotencyKey: Guid.NewGuid().ToString())).ReadAsync<JsonElement>();
        bulk.EnumerateArray().Select(r => r.GetProperty("propertyId").GetGuid()).Should().Equal(a.PropertyId);
        (await IdsAsync($"/api/v1/invoices?propertyId={b.PropertyId}", ownerToken)).Should().BeEmpty("quản lý khu A không tạo được phiếu khu B");

        // Chủ trọ thấy cả hai khu.
        (await IdsAsync("/api/v1/properties", ownerToken)).Should().Contain([a.PropertyId, b.PropertyId]);
        (await StatusAsync($"/api/v1/contracts/{b.ContractId}", ownerToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task S3_ChangingAssignment_TakesEffectInOpenSession()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerToken = owner.Tokens.AccessToken;
        var a = await _client.CreatePropertyAsync(ownerToken);
        var b = await _client.CreatePropertyAsync(ownerToken);
        var (managerId, token) = await ManagerAsync(ownerToken, a);

        var changed = await _client.PutJsonAsync($"/api/v1/org/members/{managerId}/properties", new { propertyIds = new[] { b } }, ownerToken);
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.ReadAsync<JsonElement>()).GetProperty("propertyIds").EnumerateArray().Select(p => p.GetGuid()).Should().Equal(b);

        (await IdsAsync("/api/v1/properties", token)).Should().Equal(b);
        (await StatusAsync($"/api/v1/properties/{a}", token)).Should().Be(HttpStatusCode.NotFound);

        await _client.PutJsonAsync($"/api/v1/org/members/{managerId}/properties", new { propertyIds = Array.Empty<Guid>() }, ownerToken);
        (await IdsAsync("/api/v1/properties", token)).Should().BeEmpty();
    }

    [Fact]
    public async Task S4_Renter_OutsideScopeHidden_DuplicateIdNumberClaimsProfile_OtherPropertyHistoryStaysHidden()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerToken = owner.Tokens.AccessToken;
        var b = await SiteAsync(ownerToken);
        var a = await _client.CreatePropertyAsync(ownerToken, anchorDay: TestData.StartWithinOnePeriod(TestData.Today(factory), 3).Day);
        var (_, token) = await ManagerAsync(ownerToken, a);

        (await StatusAsync($"/api/v1/renters/{b.RenterId}", token)).Should().Be(HttpStatusCode.NotFound);
        var own = await _client.CreateRenterAsync(token);
        (await StatusAsync($"/api/v1/renters/{own}", token)).Should().Be(HttpStatusCode.OK, "hồ sơ chính mình tạo");

        // Người thuê cũ của khu B sang thuê khu A: nhập CCCD ⇒ 409 kèm họ tên + 4 số cuối, từ đó dùng được hồ sơ.
        var duplicate = await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Trần Thị Lan", dateOfBirth = "2000-04-15", gender = "Female", nationality = "VN",
            idType = "CitizenId", idNumber = b.IdNumber, permanentAddress = "Nam Định"
        }, token, idempotencyKey: Guid.NewGuid().ToString());
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await duplicate.ReadAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("RENTER_ID_NUMBER_EXISTS");
        problem.GetProperty("existingRenterId").GetGuid().Should().Be(b.RenterId);
        problem.GetProperty("existingRenterName").GetString().Should().Be("Trần Thị Lan");
        problem.GetProperty("existingIdNumberLast4").GetString().Should().Be(b.IdNumber[^4..]);

        (await StatusAsync($"/api/v1/renters/{b.RenterId}", token)).Should().Be(HttpStatusCode.OK);
        var room = await _client.CreateRoomAsync(token, a);
        var contract = await _client.PostContractAsync(token, room, b.RenterId, TestData.StartWithinOnePeriod(TestData.Today(factory), 3));
        contract.StatusCode.Should().Be(HttpStatusCode.Created, await contract.Content.ReadAsStringAsync());

        (await IdsAsync("/api/v1/contracts", token)).Should().NotContain(b.ContractId, "hợp đồng ở khu B vẫn ẩn");
        (await StatusAsync($"/api/v1/contracts/{b.ContractId}", token)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task S5_Manager_HasOwnerPowersInsideProperty()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var a = await SiteAsync(owner.Tokens.AccessToken);
        var (_, token) = await ManagerAsync(owner.Tokens.AccessToken, a.PropertyId);

        var me = await (await _client.GetAsync("/api/v1/me", token)).ReadAsync<JsonElement>();
        me.GetProperty("canViewSensitiveData").GetBoolean().Should().BeTrue();
        me.GetProperty("canWriteOff").GetBoolean().Should().BeTrue();

        var revealed = await _client.PostJsonAsync($"/api/v1/renters/{a.RenterId}/reveal-id-number", null, token);
        revealed.StatusCode.Should().Be(HttpStatusCode.OK, await revealed.Content.ReadAsStringAsync());
        (await revealed.ReadAsync<JsonElement>()).GetProperty("idNumber").GetString().Should().Be(a.IdNumber);
        (await _client.PostJsonAsync($"/api/v1/properties/{a.PropertyId}/lessor/reveal-id-number", null, token))
            .StatusCode.Should().Be(HttpStatusCode.OK, "ký thay hợp đồng cần số giấy tờ bên cho thuê");
        (await StatusAsync($"/api/v1/properties/{a.PropertyId}/rooms/import-template", token)).Should().Be(HttpStatusCode.OK, "nhập Excel vào khu mình");
        (await _client.PutJsonAsync($"/api/v1/properties/{a.PropertyId}/house-rules", new { houseRules = "Giữ trật tự sau 22h" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task S6_OwnerOnlyActions_AreForbiddenForManager()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var a = await SiteAsync(owner.Tokens.AccessToken);
        var (managerId, token) = await ManagerAsync(owner.Tokens.AccessToken, a.PropertyId);
        var empty = new { };

        var calls = new (string Method, string Url)[]
        {
            ("POST", "/api/v1/properties"),
            ("POST", $"/api/v1/properties/{a.PropertyId}/archive"),
            ("POST", $"/api/v1/properties/{a.PropertyId}/restore"),
            ("PUT", $"/api/v1/properties/{a.PropertyId}/lessor"),
            ("DELETE", $"/api/v1/properties/{a.PropertyId}/lessor"), // M-28: trước đây thiếu policy OrgOwner
            ("PUT", $"/api/v1/properties/{a.PropertyId}/bank-account"),
            ("POST", "/api/v1/contract-templates"),
            ("PUT", "/api/v1/org"),
            ("PUT", "/api/v1/org/lessor"),
            ("PUT", "/api/v1/org/data-retention"),
            ("POST", $"/api/v1/renters/{a.RenterId}/anonymize"),
            ("POST", "/api/v1/org/members"),
            ("PUT", $"/api/v1/org/members/{managerId}/properties"),
            ("GET", "/api/v1/audit-logs"),
            ("GET", "/api/v1/org/login-events"),
        };
        foreach (var (method, url) in calls)
        {
            var response = method switch
            {
                "GET" => await _client.GetAsync(url, token),
                "PUT" => await _client.PutJsonAsync(url, empty, token),
                "DELETE" => await _client.DeleteAsync(url, token),
                _ => await _client.PostJsonAsync(url, empty, token, idempotencyKey: Guid.NewGuid().ToString())
            };
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {url}");
        }
    }

    [Fact]
    public async Task S8_Assignment_Validation_And_MemberList()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerToken = owner.Tokens.AccessToken;
        var a = await _client.CreatePropertyAsync(ownerToken);
        var archived = await _client.CreatePropertyAsync(ownerToken);
        (await _client.PostJsonAsync($"/api/v1/properties/{archived}/archive", null, ownerToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var other = await _client.CreateActiveOwnerAsync();
        var foreign = await _client.CreatePropertyAsync(other.Tokens.AccessToken);
        var (managerId, _) = await ManagerAsync(ownerToken, a);
        var url = $"/api/v1/org/members/{managerId}/properties";

        foreach (var invalid in new[] { foreign, archived, Guid.NewGuid() })
            (await (await _client.PutJsonAsync(url, new { propertyIds = new[] { a, invalid } }, ownerToken)).ReadProblemCodeAsync())
                .Should().Be("PROPERTY_ASSIGNMENT_INVALID");
        var ownerId = (await (await _client.GetAsync("/api/v1/me", ownerToken)).ReadAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await (await _client.PutJsonAsync($"/api/v1/org/members/{ownerId}/properties", new { propertyIds = new[] { a } }, ownerToken)).ReadProblemCodeAsync())
            .Should().Be("CANNOT_MODIFY_OWNER");

        var members = await (await _client.GetAsync("/api/v1/org/members", ownerToken)).ReadAsync<JsonElement>();
        var manager = members.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == managerId);
        manager.GetProperty("propertyIds").EnumerateArray().Select(p => p.GetGuid()).Should().Equal(a);
        members.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == ownerId).GetProperty("propertyIds").ValueKind
            .Should().Be(JsonValueKind.Null, "chủ trọ: mọi khu");

        // Không gửi propertyIds khi tạo ⇒ giao mọi khu đang dùng (như chuyển đổi tài khoản cũ — ID-BR-29).
        var created = await (await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Quản Lý Hai", phone = TestApi.NewPhone() }, ownerToken))
            .ReadAsync<TemporaryCredentialsResponse>();
        var second = (await (await _client.GetAsync($"/api/v1/org/members/{created.UserId}", ownerToken)).ReadAsync<JsonElement>()).GetProperty("propertyIds");
        second.EnumerateArray().Select(p => p.GetGuid()).Should().Equal(a);
    }
}

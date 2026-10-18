using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 11/10/2026 — M01 ID-UC-03 / ID-UC-12 (chủ trọ tự sửa tổ chức, tự sửa hồ sơ), M03 RT-UC-04 (lịch sử thuê theo phòng, tìm theo tên),
/// M06 MT-UC-06 (hủy chỉ số ghi nhầm).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SelfServiceAndHistoryTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> RenterAsync(string token, string fullName) =>
        await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName, dateOfBirth = "1995-05-05", gender = "Male", idType = "CitizenId", idNumber = TestData.NewCitizenId()
        }, token)).ReadIdAsync();

    [Fact]
    public async Task Owner_EditsOwnProfileAndOrganization_ChangingLoginNameForcesReLogin()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var me = await (await _client.GetAsync("/api/v1/me", token)).ReadAsync<JsonElement>();

        // Đổi họ tên (giữ SĐT) ⇒ phiên vẫn dùng được.
        (await _client.PutJsonAsync("/api/v1/me", new { fullName = "Nguyễn Văn Chủ Mới", phone = me.GetProperty("phone").GetString(), email = (string?)null }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.GetAsync("/api/v1/me", token)).ReadAsync<JsonElement>()).GetProperty("fullName").GetString().Should().Be("Nguyễn Văn Chủ Mới");
        (await (await _client.PutJsonAsync("/api/v1/me", new { fullName = "X", phone = (string?)null, email = (string?)null }, token)).ReadProblemCodeAsync())
            .Should().NotBeNull("phải còn SĐT hoặc email để đăng nhập");

        // Thông tin tổ chức: chủ trọ sửa thoải mái, mã tổ chức giữ nguyên.
        var organization = await (await _client.GetAsync("/api/v1/org", token)).ReadAsync<JsonElement>();
        var updated = await _client.PutJsonAsync("/api/v1/org", new
        {
            name = "Nhà trọ Hoa Sữa", contactName = "Chị Hoa", contactPhone = "0912345678", contactEmail = (string?)null, taxCode = (string?)null,
            address = "12 Tạ Quang Bửu"
        }, token);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var after = await (await _client.GetAsync("/api/v1/org", token)).ReadAsync<JsonElement>();
        after.GetProperty("name").GetString().Should().Be("Nhà trọ Hoa Sữa");
        after.GetProperty("code").GetString().Should().Be(organization.GetProperty("code").GetString());
        (await (await _client.GetAsync("/api/v1/me", token)).ReadAsync<JsonElement>()).GetProperty("organization").GetProperty("name").GetString()
            .Should().Be("Nhà trọ Hoa Sữa");

        // Phó quản lý xem được nhưng không sửa được tổ chức.
        var created = await (await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, token))
            .ReadAsync<JsonElement>();
        var managerLogin = await _client.LoginAsync(created.GetProperty("username").GetString()!, created.GetProperty("temporaryPassword").GetString()!);
        (await _client.PutJsonAsync("/api/v1/org", new { name = "Tên khác" }, managerLogin.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Đổi SĐT (tên đăng nhập) ⇒ phiên cũ bị vô hiệu, đăng nhập lại bằng số mới.
        // ID-UC-12 (13/10/2026): phó quản lý không tự sửa hồ sơ; chủ trọ đổi SĐT phải nhập đúng mật khẩu hiện tại.
        var managerTokens = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = created.GetProperty("temporaryPassword").GetString(), newPassword = "Pho-Quan-Ly-2026!" }, managerLogin.AccessToken);
        var managerToken = (await managerTokens.ReadAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var selfEdit = await _client.PutJsonAsync("/api/v1/me",
            new { fullName = "Tự Đổi Tên", phone = created.GetProperty("username").GetString(), email = (string?)null }, managerToken);
        selfEdit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await selfEdit.ReadProblemCodeAsync()).Should().Be("PROFILE_MANAGED_BY_OWNER");

        var newPhone = TestApi.NewPhone();
        Task<HttpResponseMessage> ChangePhoneAsync(string? currentPassword) => _client.PutJsonAsync("/api/v1/me",
            new { fullName = "Nguyễn Văn Chủ Mới", phone = newPhone, email = (string?)null, currentPassword }, token);
        (await (await ChangePhoneAsync(null)).ReadProblemCodeAsync()).Should().Be("CURRENT_PASSWORD_REQUIRED");
        (await (await ChangePhoneAsync("Sai-mat-khau-123")).ReadProblemCodeAsync()).Should().Be("INVALID_CURRENT_PASSWORD");
        (await _client.GetAsync("/api/v1/me", token)).StatusCode.Should().Be(HttpStatusCode.OK, "đổi không thành thì phiên vẫn còn");
        (await ChangePhoneAsync(owner.Password)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync("/api/v1/me", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.LoginAsync(newPhone, owner.Password)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RoomTenancyHistory_ListsEveryStayWithPeopleAndRoles_AndSearchByNameFindsRooms()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = today.AddDays(-40);
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomA = await _client.CreateRoomAsync(token, propertyId, "A1");
        var roomB = await _client.CreateRoomAsync(token, propertyId, "A2");
        var binh = await RenterAsync(token, "Tô Văn Bình");
        var hanh = await RenterAsync(token, "Tô Thị Hạnh");
        var first = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = roomA,
            contract = new
            {
                representativeRenterId = binh, startDate = start, monthlyRent = 3_500_000, depositAmount = 0,
                occupants = new object[] { new { renterId = binh }, new { renterId = hanh, relationshipType = "Sibling", role = "CoTenant" } }
            }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{first}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // HĐ chuyển sang A2 hôm nay (ngày chuyển luôn là hôm nay — CT-BR-14).
        (await _client.PostJsonAsync($"/api/v1/contracts/{first}/transfer-room", new { toRoomId = roomB }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await (await _client.GetAsync($"/api/v1/rooms/{roomA}/tenancy-history", token)).ReadAsync<List<JsonElement>>();
        history.Select(h => h.GetProperty("contractId").GetGuid()).Should().Equal([first]);
        var earlier = history[0];
        earlier.GetProperty("fromDate").GetString().Should().Be($"{start:yyyy-MM-dd}");
        earlier.GetProperty("toDate").GetString().Should().Be($"{today:yyyy-MM-dd}");
        earlier.GetProperty("isCurrent").GetBoolean().Should().BeTrue("ngày chuyển vẫn tính là đang ở phòng cũ (CT-BR-14)");
        earlier.GetProperty("representativeName").GetString().Should().Be("Tô Văn Bình");
        earlier.GetProperty("occupants").EnumerateArray().Select(o => (o.GetProperty("fullName").GetString(), o.GetProperty("role").GetString()))
            .Should().BeEquivalentTo([("Tô Văn Bình", "Representative"), ("Tô Thị Hạnh", "CoTenant")]);

        var found = await (await _client.GetAsync("/api/v1/tenancy-history?search=hanh", token)).ReadAsync<List<JsonElement>>();
        found.Select(f => (f.GetProperty("roomCode").GetString(), f.GetProperty("toDate").ValueKind == JsonValueKind.Null))
            .Should().BeEquivalentTo([("A2", true), ("A1", false)], "Hạnh từng ở A1, đang ở A2");
        (await _client.GetAsync("/api/v1/tenancy-history?search=h", token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VoidReading_OnlyPeriodicOrAdhoc_AndNotWhenLocked()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Postpaid");
        var electricity = await _client.FeeIdAsync(token, propertyId, "Điện");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = month.AddDays(-30), unitPrice = 3500 }, token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var meterId = await _client.InstallMeterAsync(token, roomId, electricity, month.AddDays(-1), 100);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), month);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate",
            new { handoverReadings = new[] { new { meterId, value = (decimal?)null } } }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Task<HttpResponseMessage> SaveAsync(decimal value) => _client.PutJsonAsync($"/api/v1/properties/{propertyId}/meter-readings", new
        {
            readings = new[] { new { meterId, contractId, closingPeriodStart = month, readingDate = month.AddMonths(1), value } }
        }, token);
        async Task<Guid> ReadingIdAsync(string kind) =>
            (await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>())
            .Single(r => r.GetProperty("kind").GetString() == kind).GetProperty("id").GetGuid();
        Task<HttpResponseMessage> VoidAsync(Guid readingId, string? reason = "Ghi nhầm phòng") =>
            _client.PostJsonAsync($"/api/v1/meter-readings/{readingId}/void", new { reason }, token);

        (await SaveAsync(9_999)).StatusCode.Should().Be(HttpStatusCode.OK); // ghi nhầm
        var wrong = await ReadingIdAsync("Periodic");
        (await (await VoidAsync(await ReadingIdAsync("Handover"))).ReadProblemCodeAsync()).Should().Be("READING_NOT_VOIDABLE");
        (await VoidAsync(wrong)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.GetAsync($"/api/v1/meters/{meterId}/readings", token)).ReadAsync<List<JsonElement>>())
            .Should().NotContain(r => r.GetProperty("kind").GetString() == "Periodic");

        // Ghi lại đúng số, chốt phiếu ⇒ chỉ số bị khóa, không hủy được.
        (await SaveAsync(160)).StatusCode.Should().Be(HttpStatusCode.OK);
        await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{month:yyyy-MM}", roomIds = new[] { roomId } }, token);
        var page = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        (await _client.PostJsonAsync($"/api/v1/invoices/{page.GetProperty("items")[0].GetProperty("id").GetGuid()}/finalize", null, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await VoidAsync(await ReadingIdAsync("Periodic"))).ReadProblemCodeAsync()).Should().Be("READING_LOCKED");
    }

    /// <summary>RT-UC-04 (13/10/2026): người đứng tên trong lịch sử phòng lấy theo thời điểm, không lấy người hiện tại cho giai đoạn cũ.</summary>
    [Fact]
    public async Task RoomTenancyHistory_ShowsWhoWasRepresentativeWhen_AfterRepresentativeChange()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = today.AddDays(-40);
        var changedOn = today.AddDays(-10);
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId, "A1");
        var father = await RenterAsync(token, "Đào Văn Cha");
        var binh = await RenterAsync(token, "Tô Văn Bình");
        var hanh = await RenterAsync(token, "Tô Thị Hạnh");
        // Bố đứng tên nhưng không ở; Bình và Hạnh ở. Sau 30 ngày Hạnh đứng tên thay.
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = father, startDate = start, monthlyRent = 3_500_000, depositAmount = 0,
                occupants = new object[] { new { renterId = binh, relationshipType = "Child" }, new { renterId = hanh, relationshipType = "Child" } }
            }
        }, token)).ReadIdAsync();
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var changed = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/representative", new { renterId = hanh, date = changedOn }, token);
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());

        var stay = (await (await _client.GetAsync($"/api/v1/rooms/{roomId}/tenancy-history", token)).ReadAsync<List<JsonElement>>()).Single();
        stay.GetProperty("representativeName").GetString().Should().Be("Tô Thị Hạnh", "người đứng tên cuối lần ở");
        stay.GetProperty("representatives").EnumerateArray()
            .Select(r => (r.GetProperty("fullName").GetString(), r.GetProperty("fromDate").GetString(),
                r.GetProperty("toDate").ValueKind == JsonValueKind.Null ? null : r.GetProperty("toDate").GetString()))
            .Should().Equal(
                ("Đào Văn Cha", $"{start:yyyy-MM-dd}", $"{changedOn.AddDays(-1):yyyy-MM-dd}"),
                ("Tô Thị Hạnh", $"{changedOn:yyyy-MM-dd}", null));
        stay.GetProperty("occupants").EnumerateArray().Select(o => (o.GetProperty("fullName").GetString(), o.GetProperty("role").GetString()))
            .Should().BeEquivalentTo([("Tô Văn Bình", "Resident"), ("Tô Thị Hạnh", "Representative")]);

        // Tìm theo tên: người từng đứng tên (không ở) vẫn ra, đúng khoảng ngày đứng tên.
        var found = (await (await _client.GetAsync("/api/v1/tenancy-history?search=dao van cha", token)).ReadAsync<List<JsonElement>>()).Single();
        found.GetProperty("role").GetString().Should().Be("Representative");
        found.GetProperty("fromDate").GetString().Should().Be($"{start:yyyy-MM-dd}");
        found.GetProperty("toDate").GetString().Should().Be($"{changedOn.AddDays(-1):yyyy-MM-dd}");
    }
}

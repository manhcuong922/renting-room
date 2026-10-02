using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AdminOrganizationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateOrganization_Returns201_WithLocation_AndNoStore()
    {
        var admin = await _client.LoginAdminAsync();

        var response = await _client.PostJsonAsync("/api/v1/admin/organizations", new
        {
            code = TestApi.NewOrganizationCode(),
            name = "Nhà trọ Cầu Giấy",
            owner = new { fullName = "Nguyễn Văn Minh", phone = TestApi.NewPhone() }
        }, admin.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var created = await response.ReadAsync<CreatedOrganization>();
        response.Headers.Location!.ToString().Should().EndWith(created.OrganizationId.ToString());
        created.TemporaryPassword.Should().HaveLength(12);

        var detail = await _client.GetAsync(response.Headers.Location!.ToString(), admin.AccessToken);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrganization_Returns409_WhenOwnerPhoneAlreadyUsed()
    {
        var admin = await _client.LoginAdminAsync();
        var phone = TestApi.NewPhone();
        await _client.CreateOrganizationAsync(admin.AccessToken, phone);

        var duplicate = await _client.PostJsonAsync("/api/v1/admin/organizations", new
        {
            code = TestApi.NewOrganizationCode(),
            name = "Tổ chức khác",
            owner = new { fullName = "Người khác", phone }
        }, admin.AccessToken);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("PHONE_TAKEN");
    }

    [Fact]
    public async Task CreateOrganization_ConcurrentSamePhone_OnlyOneSucceeds()
    {
        var admin = await _client.LoginAdminAsync();
        var phone = TestApi.NewPhone();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            factory.CreateClient().PostJsonAsync("/api/v1/admin/organizations", new
            {
                code = TestApi.NewOrganizationCode(),
                name = "Song song",
                owner = new { fullName = "Chủ", phone }
            }, admin.AccessToken)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Suspend_RevokesOwnerSessions_AndBlocksLogin_UntilReactivated()
    {
        var admin = await _client.LoginAdminAsync();
        var owner = await _client.CreateActiveOwnerAsync();
        var organizationUrl = $"/api/v1/admin/organizations/{owner.Organization.OrganizationId}";

        var suspend = await _client.PostJsonAsync($"{organizationUrl}/suspend", new { reason = "Chưa thanh toán phí" }, admin.AccessToken);
        suspend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var oldToken = await _client.GetAsync("/api/v1/rooms", owner.Tokens.AccessToken);
        (await oldToken.ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");

        var login = await _client.PostJsonAsync("/api/v1/auth/login", new { username = owner.Phone, password = owner.Password });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await login.ReadProblemCodeAsync()).Should().Be("ORGANIZATION_SUSPENDED");

        var suspendAgain = await _client.PostJsonAsync($"{organizationUrl}/suspend", new { reason = "Lần hai" }, admin.AccessToken);
        (await suspendAgain.ReadProblemCodeAsync()).Should().Be("ORG_ALREADY_SUSPENDED");

        var reactivate = await _client.PostJsonAsync($"{organizationUrl}/reactivate", null, admin.AccessToken);
        reactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync("/api/v1/auth/login", new { username = owner.Phone, password = owner.Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListOrganizations_FiltersBySearchAndStatus_WithPaging()
    {
        var admin = await _client.LoginAdminAsync();
        var code = TestApi.NewOrganizationCode();
        var created = await _client.PostJsonAsync("/api/v1/admin/organizations", new
        {
            code,
            name = "Nhà trọ Tìm Kiếm",
            owner = new { fullName = "Chủ", phone = TestApi.NewPhone() }
        }, admin.AccessToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var bySearch = await _client.GetAsync($"/api/v1/admin/organizations?search={code.ToLowerInvariant()}&status=Active&page=1&pageSize=5", admin.AccessToken);
        bySearch.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await bySearch.ReadAsync<PageResponse>();
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle(o => o.Code == code);
        page.PageSize.Should().Be(5);

        var suspendedOnly = await _client.GetAsync($"/api/v1/admin/organizations?search={code}&status=Suspended", admin.AccessToken);
        (await suspendedOnly.ReadAsync<PageResponse>()).TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    public async Task ListOrganizations_Returns400_ForInvalidPaging(string query)
    {
        var admin = await _client.LoginAdminAsync();

        var response = await _client.GetAsync($"/api/v1/admin/organizations?{query}", admin.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
    }

    private sealed record OrganizationItem(Guid Id, string Code, string Name, string Status);

    private sealed record PageResponse(List<OrganizationItem> Items, int Page, int PageSize, int TotalCount);

    [Fact]
    public async Task GetOrganization_Returns404_ForUnknownId()
    {
        var admin = await _client.LoginAdminAsync();

        var response = await _client.GetAsync($"/api/v1/admin/organizations/{Guid.NewGuid()}", admin.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemCodeAsync()).Should().Be("ORGANIZATION_NOT_FOUND");
    }
}

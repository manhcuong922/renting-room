using System.Net;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

public sealed record MemberResponse(Guid Id, string FullName, string? Phone, string Role, string Status, bool MustChangePassword, string Version);

public sealed record TemporaryCredentialsResponse(Guid UserId, string Username, string TemporaryPassword, DateTimeOffset ExpiresAt);

/// <summary>M01 §3.3: phó quản lý — chủ trọ quản lý, phó quản lý thao tác nghiệp vụ như chủ trọ.</summary>
[Collection(ApiCollection.Name)]
public sealed class MemberTests(ApiFactory factory)
{
    private const string MembersUrl = "/api/v1/org/members";
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(TemporaryCredentialsResponse Credentials, TokenResponse Tokens)> AddActiveManagerAsync(OwnerAccount owner)
    {
        var created = await _client.PostJsonAsync(MembersUrl, new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, owner.Tokens.AccessToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();

        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        var changed = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = "PhoQuanLy2026" }, login.AccessToken);
        return (credentials, await changed.ReadAsync<TokenResponse>());
    }

    [Fact]
    public async Task Manager_CanOperateBusinessData_LikeOwner()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var (_, manager) = await AddActiveManagerAsync(owner);

        var propertyId = await _client.CreatePropertyAsync(manager.AccessToken);
        await _client.CreateRoomAsync(manager.AccessToken, propertyId);

        // Chủ trọ thấy dữ liệu phó quản lý tạo (cùng tổ chức).
        (await _client.GetAsync($"/api/v1/properties/{propertyId}", owner.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var members = await (await _client.GetAsync(MembersUrl, manager.AccessToken)).ReadAsync<List<MemberResponse>>();
        members.Select(m => m.Role).Should().BeEquivalentTo(["OrgOwner", "OrgManager"]);
    }

    [Fact]
    public async Task Manager_CannotManageMembers()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var (_, manager) = await AddActiveManagerAsync(owner);

        var response = await _client.PostJsonAsync(MembersUrl, new { fullName = "X", phone = TestApi.NewPhone() }, manager.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LockingManager_KicksOutSessionImmediately_UnlockRestoresLogin()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var (credentials, manager) = await AddActiveManagerAsync(owner);

        (await _client.PostJsonAsync($"{MembersUrl}/{credentials.UserId}/lock", null, owner.Tokens.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await (await _client.GetAsync("/api/v1/me", manager.AccessToken)).ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");
        (await _client.PostJsonAsync("/api/v1/auth/login", new { username = credentials.Username, password = "PhoQuanLy2026" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await _client.PostJsonAsync($"{MembersUrl}/{credentials.UserId}/unlock", null, owner.Tokens.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostJsonAsync("/api/v1/auth/login", new { username = credentials.Username, password = "PhoQuanLy2026" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemovingManager_IsPermanent_AndFreesPhoneNumber()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var (credentials, _) = await AddActiveManagerAsync(owner);

        (await _client.PostJsonAsync($"{MembersUrl}/{credentials.UserId}/remove", null, owner.Tokens.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await (await _client.GetAsync(MembersUrl, owner.Tokens.AccessToken)).ReadAsync<List<MemberResponse>>();
        members.Should().NotContain(m => m.Id == credentials.UserId);

        // SĐT được giải phóng — thêm lại người khác với cùng SĐT được.
        var again = await _client.PostJsonAsync(MembersUrl, new { fullName = "Người mới", phone = credentials.Username }, owner.Tokens.AccessToken);
        again.StatusCode.Should().Be(HttpStatusCode.Created);

        var unlockRemoved = await _client.PostJsonAsync($"{MembersUrl}/{credentials.UserId}/unlock", null, owner.Tokens.AccessToken);
        (await unlockRemoved.ReadProblemCodeAsync()).Should().Be("USER_NOT_LOCKED");
    }

    [Fact]
    public async Task OwnerCannotBeModifiedThroughMemberEndpoints()
    {
        var owner = await _client.CreateActiveOwnerAsync();

        var response = await _client.PostJsonAsync($"{MembersUrl}/{owner.Organization.OwnerUserId}/lock", null, owner.Tokens.AccessToken);

        (await response.ReadProblemCodeAsync()).Should().Be("CANNOT_MODIFY_OWNER");
    }

    [Fact]
    public async Task OtherOrganizationsMember_IsNotFound()
    {
        var ownerA = await _client.CreateActiveOwnerAsync();
        var ownerB = await _client.CreateActiveOwnerAsync();
        var (credentials, _) = await AddActiveManagerAsync(ownerA);

        var response = await _client.PostJsonAsync($"{MembersUrl}/{credentials.UserId}/lock", null, ownerB.Tokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetPassword_GivesNewTemporaryPassword_AndRevokesSessions()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var (credentials, manager) = await AddActiveManagerAsync(owner);

        var reset = await _client.PostJsonAsync($"{MembersUrl}/{credentials.UserId}/reset-password", null, owner.Tokens.AccessToken);
        reset.Headers.CacheControl!.NoStore.Should().BeTrue();
        var newCredentials = await reset.ReadAsync<TemporaryCredentialsResponse>();

        (await (await _client.GetAsync("/api/v1/me", manager.AccessToken)).ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");
        (await _client.LoginAsync(credentials.Username, newCredentials.TemporaryPassword)).MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task TemporaryPassword_ExpiresAfter72Hours()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var created = await _client.PostJsonAsync(MembersUrl, new { fullName = "Chậm đăng nhập", phone = TestApi.NewPhone() }, owner.Tokens.AccessToken);
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();

        credentials.ExpiresAt.Should().BeCloseTo(factory.Clock.GetUtcNow().AddHours(72), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Admin_CanResetOwnerPassword_ButCannotLockOwner()
    {
        var admin = await _client.LoginAdminAsync();
        var owner = await _client.CreateActiveOwnerAsync();
        var ownerId = owner.Organization.OwnerUserId;

        var reset = await _client.PostJsonAsync($"/api/v1/admin/users/{ownerId}/reset-password", null, admin.AccessToken);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _client.GetAsync("/api/v1/me", owner.Tokens.AccessToken)).ReadProblemCodeAsync()).Should().Be("SESSION_REVOKED");

        var lockOwner = await _client.PostJsonAsync($"/api/v1/admin/users/{ownerId}/lock", null, admin.AccessToken);
        (await lockOwner.ReadProblemCodeAsync()).Should().Be("CANNOT_LOCK_OWNER");
    }
}

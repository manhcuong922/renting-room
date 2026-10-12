using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;
using renting_room.Infrastructure.Jobs;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 12/10/2026 — M01: đăng xuất 1 thiết bị vô hiệu ngay access token của phiên đó; nhật ký đăng nhập; dọn refresh token hết hạn;
/// số khu / phòng trong danh sách tổ chức của SystemAdmin.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountSecurityTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Logout_InvalidatesAccessTokenOfThatSessionOnly()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var otherDevice = await _client.LoginAsync(owner.Phone, owner.Password);
        (await _client.GetAsync("/api/v1/me", owner.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = owner.Tokens.RefreshToken }, owner.Tokens.AccessToken))
            .IsSuccessStatusCode.Should().BeTrue();

        (await _client.GetAsync("/api/v1/me", owner.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "access token của phiên vừa đăng xuất hết hiệu lực ngay, không chờ hết hạn");
        (await _client.GetAsync("/api/v1/me", otherDevice.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK, "thiết bị khác không bị ảnh hưởng");
    }

    [Fact]
    public async Task LoginEvents_RecordSuccessAndFailure_OwnerOnly()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        (await _client.PostJsonAsync("/api/v1/auth/login", new { username = owner.Phone, password = "Sai-mat-khau-123" }, null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _client.LoginAsync(owner.Phone, owner.Password);

        var events = (await (await _client.GetAsync("/api/v1/org/login-events?pageSize=50", token)).ReadAsync<JsonElement>()).GetProperty("items")
            .EnumerateArray().ToList();
        events.Select(e => e.GetProperty("reason").GetString()).Should().Contain(["Success", "WrongPassword"]);
        events.Should().OnlyContain(e => e.GetProperty("userId").GetGuid() == owner.Organization.OwnerUserId);
        var failed = (await (await _client.GetAsync("/api/v1/org/login-events?success=false", token)).ReadAsync<JsonElement>()).GetProperty("items");
        failed.EnumerateArray().Should().OnlyContain(e => !e.GetProperty("success").GetBoolean());

        var created = await (await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, token))
            .ReadAsync<JsonElement>();
        var manager = await _client.LoginAsync(created.GetProperty("username").GetString()!, created.GetProperty("temporaryPassword").GetString()!);
        (await _client.GetAsync("/api/v1/org/login-events", manager.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AccountCleanup_DeletesLongExpiredTokensAndOldLoginEvents_KeepsLiveSession()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var now = factory.Clock.GetUtcNow();
        var longAgo = now.AddDays(-200);
        var staleToken = RefreshToken.Issue(owner.Organization.OwnerUserId, Guid.NewGuid(), $"stale-{Guid.NewGuid():N}", Guid.NewGuid(), longAgo,
            TimeSpan.FromDays(1), longAgo.AddDays(2), null);
        var staleEvent = LoginEvent.Record(null, "0900000000", null, LoginOutcome.UnknownUser, longAgo);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.RefreshTokens.Add(staleToken);
            db.LoginEvents.Add(staleEvent);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var purged = await factory.Services.GetRequiredService<AccountCleanupService>().RunAsync(now, CancellationToken.None);
        purged.Tokens.Should().BeGreaterThanOrEqualTo(1);
        purged.LoginEvents.Should().BeGreaterThanOrEqualTo(1);

        await using var check = factory.Services.CreateAsyncScope();
        var after = check.ServiceProvider.GetRequiredService<IAppDbContext>();
        (await after.RefreshTokens.AnyAsync(t => t.Id == staleToken.Id)).Should().BeFalse();
        (await after.LoginEvents.AnyAsync(e => e.Id == staleEvent.Id)).Should().BeFalse();
        (await _client.GetAsync("/api/v1/me", owner.Tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK, "phiên đang dùng không bị dọn");
    }

    [Fact]
    public async Task AdminOrganizationList_ShowsPropertyAndRoomCounts()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        await _client.CreateRoomAsync(token, propertyId);
        await _client.CreateRoomAsync(token, propertyId);
        var admin = await _client.LoginAdminAsync();

        var page = await (await _client.GetAsync("/api/v1/admin/organizations?pageSize=100", admin.AccessToken)).ReadAsync<JsonElement>();
        var organization = page.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == owner.Organization.OrganizationId);
        organization.GetProperty("propertyCount").GetInt32().Should().Be(1);
        organization.GetProperty("roomCount").GetInt32().Should().Be(2);
    }
}

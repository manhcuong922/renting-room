using System.Net;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;
using renting_room.Infrastructure.Persistence;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M-18 (11/11/2026): admin bootstrap (mật khẩu đặt qua biến môi trường lúc triển khai) phải đổi mật khẩu ở lần đăng nhập đầu —
/// trước đó mật khẩu trong <c>.env</c> dùng được mãi mãi, lộ <c>.env</c> / backup là mất quyền admin hệ thống.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BootstrapAdminTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task BootstrapAdmin_FirstLogin_OnlyAllowsChangingPassword_ThenAdminApiWorks()
    {
        const string bootstrapPassword = "Bootstrap-Env-2026!";
        var phone = TestApi.NewPhone();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(User.CreateSystemAdmin("Admin Triển Khai", phone, null,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(bootstrapPassword)));
            await db.SaveChangesAsync();
        }

        var first = await _client.LoginAsync(phone, bootstrapPassword);
        first.MustChangePassword.Should().BeTrue();
        var blocked = await _client.GetAsync("/api/v1/admin/organizations", first.AccessToken);
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.ReadProblemCodeAsync()).Should().Be("PASSWORD_CHANGE_REQUIRED");

        var changed = await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = bootstrapPassword, newPassword = "Admin-Moi-Rat-Manh-2026" }, first.AccessToken);
        var tokens = await changed.ReadAsync<TokenResponse>();

        tokens.MustChangePassword.Should().BeFalse();
        (await _client.GetAsync("/api/v1/admin/organizations", tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Mật khẩu trong .env không còn dùng được.
        (await _client.PostJsonAsync("/api/v1/auth/login", new { username = phone, password = bootstrapPassword }, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

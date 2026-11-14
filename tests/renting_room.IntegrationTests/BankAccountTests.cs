using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// H-06 (06/11/2026, M02 §9): số tài khoản nhận tiền in trên mọi phiếu / mã QR ⇒ chỉ chủ trọ sửa, đổi / xóa phải nhập mật khẩu đăng nhập.
/// Trước đây phó quản lý đổi được sang tài khoản khác 1 kỳ ⇒ tiền cả khu chảy sai chỗ.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BankAccountTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object Account(string accountNo, string? currentPassword = null) =>
        new { bankName = "Vietcombank", accountNo, accountName = "NGUYEN VAN CHU", currentPassword };

    private async Task<string?> AccountNoAsync(string token, Guid propertyId)
    {
        var property = await (await _client.GetAsync($"/api/v1/properties/{propertyId}", token)).ReadAsync<JsonElement>();
        var account = property.GetProperty("bankAccount");
        return account.ValueKind == JsonValueKind.Null ? null : account.GetProperty("accountNo").GetString();
    }

    [Fact]
    public async Task Manager_CannotChangeBankAccount()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var propertyId = await _client.CreatePropertyAsync(owner.Tokens.AccessToken);
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/bank-account", Account("0123456789", owner.Password), owner.Tokens.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await _client.PostJsonAsync("/api/v1/org/members", new { fullName = "Phó Quản Lý", phone = TestApi.NewPhone() }, owner.Tokens.AccessToken);
        var credentials = await created.ReadAsync<TemporaryCredentialsResponse>();
        var login = await _client.LoginAsync(credentials.Username, credentials.TemporaryPassword);
        const string managerPassword = "PhoQuanLy2026";
        var manager = await (await _client.PostJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = credentials.TemporaryPassword, newPassword = managerPassword }, login.AccessToken)).ReadAsync<TokenResponse>();

        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/bank-account", Account("9999999999", managerPassword), manager.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/bank-account",
                new { bankName = (string?)null, accountNo = (string?)null, accountName = (string?)null, currentPassword = managerPassword }, manager.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await AccountNoAsync(owner.Tokens.AccessToken, propertyId)).Should().Be("0123456789");
    }

    [Fact]
    public async Task Owner_ChangeOrClear_RequiresLoginPassword_ResendingSameAccountDoesNot()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var url = $"/api/v1/properties/{propertyId}/bank-account";

        (await (await _client.PutJsonAsync(url, Account("0123456789"), token)).ReadProblemCodeAsync()).Should().Be("CURRENT_PASSWORD_REQUIRED");
        (await (await _client.PutJsonAsync(url, Account("0123456789", "SaiMatKhau2026"), token)).ReadProblemCodeAsync()).Should().Be("INVALID_CURRENT_PASSWORD");
        (await AccountNoAsync(token, propertyId)).Should().BeNull();

        (await _client.PutJsonAsync(url, Account("0123456789", owner.Password), token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AccountNoAsync(token, propertyId)).Should().Be("0123456789");

        // Gửi lại đúng tài khoản đang có (VD form lưu cả trang) ⇒ không cần mật khẩu.
        (await _client.PutJsonAsync(url, Account("0123456789"), token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await (await _client.PutJsonAsync(url, Account("9999999999"), token)).ReadProblemCodeAsync()).Should().Be("CURRENT_PASSWORD_REQUIRED");
        var clear = new { bankName = (string?)null, accountNo = (string?)null, accountName = (string?)null, currentPassword = (string?)null };
        (await (await _client.PutJsonAsync(url, clear, token)).ReadProblemCodeAsync()).Should().Be("CURRENT_PASSWORD_REQUIRED");
        (await AccountNoAsync(token, propertyId)).Should().Be("0123456789");

        (await _client.PutJsonAsync(url, clear with { currentPassword = owner.Password }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AccountNoAsync(token, propertyId)).Should().BeNull();
    }
}

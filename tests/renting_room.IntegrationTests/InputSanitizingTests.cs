using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M-25 (07/11/2026): chuẩn hóa chuỗi khi nhận JSON — bỏ ký tự điều khiển (trừ \n \r \t), U+000B / U+000C ⇒ xuống dòng.
/// Trước đây U+0000 trong ô chữ ⇒ PostgreSQL từ chối (22021) ⇒ 500 thay vì lưu được.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InputSanitizingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ControlCharactersInText_AreStrippedOrNormalized_NotServerError()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;

        var response = await _client.PostJsonAsync("/api/v1/renters",
            new
            {
                fullName = "Nguyễn\u0000 Văn\u000BA", dateOfBirth = "2000-01-01", gender = "Male",
                idType = "CitizenId", idNumber = "079200001234"
            }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var renter = await (await _client.GetAsync($"/api/v1/renters/{await response.ReadIdAsync()}", token)).ReadAsync<JsonElement>();
        renter.GetProperty("fullName").GetString().Should().Be("Nguyễn Văn\nA");
    }
}

using System.Net;
using Microsoft.AspNetCore.Hosting;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M-13 (11/11/2026): dò số giấy tờ (tìm theo số, hoặc tạo hồ sơ rồi xem có 409 lộ họ tên không) có hạn mức riêng theo user —
/// tìm theo tên / SĐT hằng ngày không bị tính. Chỉ hạ riêng hạn mức này; các hạn mức khác vẫn cao để dựng dữ liệu không bị 429.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdNumberLookupLimitTests(ApiFactory factory)
{
    private const int Limit = 2;

    [Fact]
    public async Task IdNumberProbing_SearchAndCreate_ShareOneLimitPerUser_NameSearchUnaffected()
    {
        var owner = await factory.CreateClient().CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:IdLookup:PermitLimit", Limit.ToString())).CreateClient();

        for (var i = 0; i < Limit; i++)
            (await limited.PostJsonAsync("/api/v1/renters/search", new { idNumber = $"07920000123{i}" }, token, idempotencyKey: null))
                .StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await limited.PostJsonAsync("/api/v1/renters/search", new { idNumber = "079200001239" }, token, idempotencyKey: null);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await blocked.ReadProblemCodeAsync()).Should().Be("TOO_MANY_REQUESTS");
        blocked.Headers.RetryAfter.Should().NotBeNull();

        (await limited.PostJsonAsync("/api/v1/renters/search", new { q = "Trần" }, token, idempotencyKey: null))
            .StatusCode.Should().Be(HttpStatusCode.OK, "tìm theo tên không dò được số giấy tờ");
        (await limited.PostJsonAsync("/api/v1/renters",
            new { fullName = "Thử Dò", dateOfBirth = "2000-01-01", gender = "Male", idType = "CitizenId", idNumber = "079200001238" }, token))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "tạo hồ sơ cũng là cách dò (409 lộ họ tên) nên dùng chung hạn mức");
    }
}

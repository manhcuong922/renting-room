using System.Net;
using Microsoft.AspNetCore.Hosting;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// L-15 (05/11/2026, M12 §5): ngoài Development, <c>AllowedHosts</c> phải liệt kê đúng tên miền thật — <c>"*"</c> nhận mọi Host header
/// (link đặt lại / tin nhắn dựng từ Host giả, cache nhiễm độc). Cấu hình thiếu ⇒ app dừng ngay khi khởi động.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HostFilteringTests(ApiFactory factory)
{
    [Fact]
    public void AllowedHostsWildcard_OutsideDevelopment_StopsAtStartup()
    {
        var act = () => factory.WithWebHostBuilder(b => b.UseSetting("AllowedHosts", "*")).CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AllowedHosts*");
    }

    [Fact]
    public async Task RequestWithUnknownHost_IsRejected()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Host = "tro-gia-mao.example";

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

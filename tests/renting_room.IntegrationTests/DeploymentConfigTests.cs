using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// Cấu hình triển khai (rà 07/11/2026): thiếu khóa Data Protection bền vững (M-17), bật seed demo ngoài Development (M-18),
/// <c>/health/ready</c> ẩn danh mở kết nối DB mỗi lần gọi (M-16) ⇒ phải dừng ngay khi khởi động / có cache ngắn.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeploymentConfigTests(ApiFactory factory)
{
    [Fact]
    public void MissingDataProtectionKeysPath_OutsideDevelopment_StopsAtStartup()
    {
        // Mất khóa = mọi id_number_encrypted thành rác ⇒ không chạy với khóa tạm trong bộ nhớ container.
        var act = () => factory.WithWebHostBuilder(b => b.UseSetting("DataProtection:KeysPath", "")).CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*DataProtection:KeysPath*");
    }

    [Fact]
    public void DataProtectionKeysPathMissingOnDisk_OutsideDevelopment_StopsAtStartupWithThePath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"renting_room_no_such_dir_{Guid.NewGuid():N}");

        var act = () => factory.WithWebHostBuilder(b => b.UseSetting("DataProtection:KeysPath", missing)).CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missing}*");
    }

    [Fact]
    public void DemoDataEnabled_OutsideDevelopment_StopsAtStartup()
    {
        // Seeder tạo chủ trọ với mật khẩu đã commit trong repo ⇒ chỉ được bật khi môi trường là Development.
        var act = () => factory.WithWebHostBuilder(b => b.UseSetting("Seed:DemoData:Enabled", "true")).CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Seed:DemoData*");
    }

    [Fact]
    public async Task HealthReady_IsCachedBriefly_SoAnonymousPollingDoesNotHitDependenciesEveryTime()
    {
        var counter = new CountingCheck();
        var cached = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("counter", counter, failureStatus: null, tags: ["ready"])));
        var client = cached.CreateClient();

        for (var i = 0; i < 5; i++)
            (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        counter.Calls.Should().Be(1, "5 request liên tiếp trong vài giây chỉ chạy kiểm tra một lần");

        factory.Clock.Advance(TimeSpan.FromSeconds(6)); // vừa quá thời hạn cache 5 giây; đồng hồ giả dùng chung cả collection nên không nhảy xa
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        counter.Calls.Should().Be(2, "hết hạn cache ⇒ kiểm tra lại");
    }

    [Fact]
    public async Task HealthLive_DoesNotRunDependencyChecks()
    {
        var counter = new CountingCheck();
        var client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("counter", counter, failureStatus: null, tags: ["ready"]))).CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

        counter.Calls.Should().Be(0);
    }

    private sealed class CountingCheck : IHealthCheck
    {
        private int _calls;
        public int Calls => _calls;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}

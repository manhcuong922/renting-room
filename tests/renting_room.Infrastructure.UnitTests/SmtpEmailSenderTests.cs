using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.UnitTests;

/// <summary>M-14 (11/11/2026): bộ gửi email thật — không ném lỗi ra ngoài, trả mã lỗi ổn định để lệnh gọi hủy / báo người dùng.</summary>
public sealed class SmtpEmailSenderTests
{
    private static IEmailSender Sender(Dictionary<string, string?> smtp)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=x" };
        foreach (var (key, value) in smtp)
            settings[$"Email:Smtp:{key}"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    private static readonly EmailMessage Message = new("nguoi.nhan@example.com", "Chủ đề", "Nội dung");

    [Fact]
    public async Task NotConfigured_ReturnsEmailNotConfigured_WithoutConnecting()
    {
        var sender = Sender([]);

        sender.IsConfigured.Should().BeFalse();
        var result = await sender.SendAsync(Message, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("EMAIL_NOT_CONFIGURED");
    }

    [Fact]
    public async Task HostUnreachable_ReturnsEmailSendFailed_InsteadOfThrowing()
    {
        // Cổng 1 trên máy này không có ai lắng nghe ⇒ từ chối kết nối ngay.
        var sender = Sender(new() { ["Host"] = "127.0.0.1", ["Port"] = "1", ["FromAddress"] = "he.thong@example.com", ["TimeoutSeconds"] = "3" });

        sender.IsConfigured.Should().BeTrue();
        var result = await sender.SendAsync(Message, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("EMAIL_SEND_FAILED");
    }
}

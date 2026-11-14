using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Infrastructure.Email;

/// <summary>
/// Cấu hình SMTP (<c>Email:Smtp</c>). Để trống <see cref="Host"/> / <see cref="FromAddress"/> ⇒ chưa cấu hình: gửi thư trả
/// <see cref="EmailErrors.NotConfigured"/>, app vẫn khởi động. <see cref="Password"/> CHỈ đặt qua user-secrets / biến môi trường
/// (<c>Email__Smtp__Password</c>), không ghi vào <c>appsettings*.json</c> (M11 §bí mật).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string? Host { get; init; }
    public int Port { get; init; } = 587;

    /// <summary><c>StartTls</c> (587, mặc định) · <c>SslOnConnect</c> (465) · <c>None</c> (chỉ dùng cho máy thử nội bộ).</summary>
    public SecureSocketOptions Security { get; init; } = SecureSocketOptions.StartTls;

    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "Renting Room";
    public int TimeoutSeconds { get; init; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>Gửi thư text/plain qua SMTP bằng MailKit. Không ném lỗi, không ghi nội dung thư (có mật khẩu tạm) vào log.</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
            return Result.Failure(EmailErrors.NotConfigured);

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.Body };

            using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
            if (!string.IsNullOrEmpty(settings.Username))
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            await client.SendAsync(new SendRequest(mime), cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chỉ ghi loại lỗi + máy chủ; địa chỉ nhận và nội dung thư không vào log.
            logger.LogError(ex, "Sending email via {Host}:{Port} failed", settings.Host, settings.Port);
            return Result.Failure(EmailErrors.SendFailed);
        }
    }
}

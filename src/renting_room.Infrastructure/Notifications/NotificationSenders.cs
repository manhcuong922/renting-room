using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Notifications;
using renting_room.Domain.Notifications;
using renting_room.Infrastructure.Identity;

namespace renting_room.Infrastructure.Notifications;

internal sealed class ZaloNotificationSender(IZaloBotClient client) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Zalo;
    public bool IsConfigured => client.IsConfigured;

    public Task<SendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken) =>
        client.SendMessageAsync(message.Recipient, message.Text, cancellationToken);
}

internal sealed class EmailNotificationSender(IEmailSender email) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;
    public bool IsConfigured => email.IsConfigured;

    public async Task<SendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var sent = await email.SendAsync(new EmailMessage(message.Recipient, message.Subject, message.Text), cancellationToken);
        if (sent.IsSuccess)
            return SendResult.Ok(null);
        return sent.Error == EmailErrors.NotConfigured ? SendResult.Permanent("EMAIL_NOT_CONFIGURED") : SendResult.Transient("EMAIL_SEND_FAILED");
    }
}

internal sealed class NotificationChannels(IZaloBotClient zalo, IEmailSender email) : INotificationChannels
{
    public bool ZaloConfigured => zalo.IsConfigured;
    public bool EmailConfigured => email.IsConfigured;

    public Task<string?> GetZaloBotNameAsync(CancellationToken cancellationToken) => zalo.GetBotNameAsync(cancellationToken);
}

/// <summary>L-19: HMAC-SHA256 có khóa (khóa dẫn xuất từ <c>PersonalData:HashKey</c>) — lộ DB không dựng lại / dò ngược được mã.</summary>
internal sealed class LinkCodeHasher(IOptions<PersonalDataOptions> options) : ILinkCodeHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes("zalo-link-code:" + options.Value.HashKey);

    public string Hash(string normalizedCode) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(normalizedCode))).ToLowerInvariant();
}

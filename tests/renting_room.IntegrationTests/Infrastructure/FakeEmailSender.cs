using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.IntegrationTests.Infrastructure;

/// <summary>Bộ gửi mail giả của test: ghi lại thư đã gửi; có thể giả lập chưa cấu hình SMTP hoặc server mail từ chối.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public bool IsConfigured { get; set; } = true;

    public bool FailSending { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
                return _sent.ToList();
        }
    }

    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return Task.FromResult(Result.Failure(EmailErrors.NotConfigured));
        if (FailSending)
            return Task.FromResult(Result.Failure(EmailErrors.SendFailed));
        lock (_sent)
            _sent.Add(message);
        return Task.FromResult(Result.Success());
    }

    public void Reset()
    {
        IsConfigured = true;
        FailSending = false;
        lock (_sent)
            _sent.Clear();
    }
}

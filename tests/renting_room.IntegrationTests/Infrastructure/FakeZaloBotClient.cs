using renting_room.Application.Notifications;

namespace renting_room.IntegrationTests.Infrastructure;

/// <summary>Zalo Bot giả: ghi lại tin gửi đi, trả kết quả theo kịch bản — không gọi mạng thật trong test.</summary>
public sealed class FakeZaloBotClient : IZaloBotClient
{
    private readonly List<(string ChatId, string Text)> _sent = [];

    public bool IsConfigured { get; set; } = true;

    /// <summary>Kết quả của lần gửi kế tiếp (đặt lại về Ok sau <see cref="Reset"/>).</summary>
    public Func<string, SendResult> OnSend { get; set; } = _ => SendResult.Ok("zalo-msg-1");

    public IReadOnlyList<(string ChatId, string Text)> Sent
    {
        get
        {
            lock (_sent)
                return _sent.ToList();
        }
    }

    public Task<string?> GetBotNameAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("Bot Nha Tro Test");

    public Task<ZaloUpdatesResult> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        Task.FromResult(new ZaloUpdatesResult(true, []));

    public Task<SendResult> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var result = OnSend(chatId);
        if (result.Outcome == SendOutcome.Ok)
        {
            lock (_sent)
                _sent.Add((chatId, text));
        }
        return Task.FromResult(result);
    }

    public void Reset()
    {
        IsConfigured = true;
        OnSend = _ => SendResult.Ok("zalo-msg-1");
        lock (_sent)
            _sent.Clear();
    }
}

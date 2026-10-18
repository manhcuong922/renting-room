using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace renting_room.Infrastructure.Auditing;

/// <summary>Hàng đợi trong RAM (singleton) giữa request và <see cref="AuditLogWriter"/>.</summary>
internal sealed class AuditQueue
{
    private readonly Channel<AuditLog> _channel;
    private readonly ILogger<AuditQueue> _logger;

    public AuditQueue(IOptions<AuditOptions> options, ILogger<AuditQueue> logger)
    {
        // FullMode.Wait: TryWrite trả false khi đầy (DropWrite sẽ bỏ âm thầm mà vẫn trả true).
        _channel = Channel.CreateBounded<AuditLog>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _logger = logger;
    }

    public ChannelReader<AuditLog> Reader => _channel.Reader;

    /// <summary>
    /// Không bao giờ chặn request: hàng đợi đầy (hoặc đã đóng khi app tắt) ⇒ bỏ bản ghi nhưng để lại dấu vết trong log ứng dụng.
    /// </summary>
    public void Enqueue(AuditLog log)
    {
        if (!_channel.Writer.TryWrite(log))
            _logger.LogError("Audit queue is full or closed; dropped {Action} on {EntityType} {EntityId} by user {UserId} at {OccurredAt}",
                log.Action, log.EntityType, log.EntityId, log.UserId, log.OccurredAt);
    }

    /// <summary>App đang tắt: ngừng nhận để lần vét cuối không bỏ sót bản ghi đến sau (chúng được ghi ra log thay vì mất âm thầm).</summary>
    public void Complete() => _channel.Writer.TryComplete();
}

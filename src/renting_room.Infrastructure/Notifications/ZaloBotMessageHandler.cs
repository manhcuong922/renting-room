using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using renting_room.Application.Notifications;
using renting_room.Domain.Notifications;
using renting_room.Infrastructure.Jobs;

namespace renting_room.Infrastructure.Notifications;

/// <summary>
/// NT-BR-14 / L-19: chống dò mã liên kết. Mỗi cuộc chat sai quá <see cref="PerChatLimit"/> lần / giờ ⇒ bot im lặng tới hết giờ; thêm trần
/// <see cref="GlobalLimit"/> lần sai / giờ cho cả hệ thống (kẻ tấn công đổi chat liên tục cũng không dò được). Bộ đếm trong RAM: khởi động lại
/// xóa bộ đếm, nhưng mã 8 ký tự (31^8 ≈ 8,5·10¹¹) sống 48 giờ nên không đủ để dò.
/// </summary>
public sealed class ZaloLinkAttemptLimiter
{
    public const int PerChatLimit = 5;
    public const int GlobalLimit = 200;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _perChat = new();
    private readonly Queue<DateTimeOffset> _global = new();
    private readonly object _gate = new();

    public bool IsBlocked(string chatId, DateTimeOffset now)
    {
        lock (_gate)
        {
            Trim(_global, now);
            if (_global.Count >= GlobalLimit)
                return true;
            if (!_perChat.TryGetValue(chatId, out var queue))
                return false;
            Trim(queue, now);
            if (queue.Count == 0)
                _perChat.TryRemove(chatId, out _);
            return queue.Count >= PerChatLimit;
        }
    }

    public void RecordFailure(string chatId, DateTimeOffset now)
    {
        lock (_gate)
        {
            _global.Enqueue(now);
            _perChat.GetOrAdd(chatId, _ => new Queue<DateTimeOffset>()).Enqueue(now);
        }
    }

    private static void Trim(Queue<DateTimeOffset> queue, DateTimeOffset now)
    {
        while (queue.Count > 0 && now - queue.Peek() >= Window)
            queue.Dequeue();
    }
}

/// <summary>
/// NT-UC-02 / NT-UC-03: bot chỉ hiểu mã liên kết và lệnh <c>HUY</c>. Mọi lỗi thành câu trả lời; "mã sai" và "mã hết hạn" cùng một câu (không cho dò).
/// Tra mã theo hash xuyên tổ chức, rồi chạy phần ghi trong scope của đúng tổ chức chứa hồ sơ.
/// </summary>
public sealed class ZaloBotMessageHandler(
    IServiceScopeFactory scopeFactory, ILinkCodeHasher hasher, ZaloLinkAttemptLimiter limiter, TimeProvider clock, ILogger<ZaloBotMessageHandler> logger)
{
    public const string HelpReply = "Nhắn mã liên kết 8 ký tự do chủ trọ cung cấp để nhận phiếu tiền phòng qua đây. Nhắn HUY để ngừng nhận tin.";
    public const string InvalidCodeReply = "Mã không đúng hoặc đã hết hạn. Nhờ chủ trọ tạo mã mới rồi nhắn lại.";
    public const string LinkedReply = "Đã liên kết thành công. Từ nay phiếu tiền phòng của bạn sẽ được gửi qua đây. Nhắn HUY để ngừng nhận tin.";
    public const string UnlinkedReply = "Đã ngừng gửi tin. Cần nhận lại thì nhờ chủ trọ tạo mã liên kết mới.";

    /// <returns>Câu trả lời gửi lại cuộc chat; null = im lặng.</returns>
    public async Task<string?> HandleAsync(ZaloUpdate update, CancellationToken cancellationToken)
    {
        var text = update.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.Equals("HUY", StringComparison.OrdinalIgnoreCase) || text.Equals("/huy", StringComparison.OrdinalIgnoreCase))
            return await UnlinkAsync(update.ChatId, cancellationToken);

        var code = ZaloLinkCode.Normalize(text);
        // Tin trò chuyện thường (không giống mã) không tính vào số lần sai.
        if (code.Length != ZaloLinkCode.Length || !code.All(char.IsAsciiLetterOrDigit))
            return HelpReply;

        var now = clock.GetUtcNow();
        if (limiter.IsBlocked(update.ChatId, now))
            return null;

        await using var lookupScope = scopeFactory.CreateAsyncScope();
        var lookup = lookupScope.ServiceProvider.GetRequiredService<IZaloLinkLookup>();
        var target = await lookup.FindUsableAsync(hasher.Hash(code), now, cancellationToken);
        // Dùng mã nguyên tử: hai người nhập cùng mã ⇒ chỉ một bên thành công (§10).
        if (target is null || !await lookup.TryConsumeAsync(target.CodeId, now, cancellationToken))
        {
            limiter.RecordFailure(update.ChatId, now);
            return InvalidCodeReply;
        }

        try
        {
            await using var scope = ScopeFor(target.OrganizationId);
            var linked = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new LinkRenterZaloCommand(target.RenterId, update.ChatId), cancellationToken);
            return linked.IsSuccess ? LinkedReply : InvalidCodeReply;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Zalo link failed for organization {OrganizationId}", target.OrganizationId);
            return InvalidCodeReply;
        }
    }

    private async Task<string> UnlinkAsync(string chatId, CancellationToken cancellationToken)
    {
        await using var lookupScope = scopeFactory.CreateAsyncScope();
        var targets = await lookupScope.ServiceProvider.GetRequiredService<IZaloLinkLookup>().ListByChatAsync(chatId, cancellationToken);
        foreach (var group in targets.GroupBy(t => t.OrganizationId))
        {
            try
            {
                await using var scope = ScopeFor(group.Key);
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new UnlinkZaloByChatCommand(chatId, group.Select(t => t.RenterId).ToList()), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Zalo unlink failed for organization {OrganizationId}", group.Key);
            }
        }
        return UnlinkedReply;
    }

    private AsyncServiceScope ScopeFor(Guid organizationId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(new OrganizationSystemUser(organizationId));
        return scope;
    }
}

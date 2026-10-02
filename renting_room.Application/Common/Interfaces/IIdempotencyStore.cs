namespace renting_room.Application.Common.Interfaces;

public enum IdempotencyOutcome
{
    /// <summary>Lần đầu thấy key — request được phép chạy.</summary>
    Started,

    /// <summary>Key đã xử lý xong với cùng nội dung — trả lại đúng response cũ.</summary>
    Replay,

    /// <summary>Một request khác cùng key đang chạy.</summary>
    InProgress,

    /// <summary>Key đã dùng cho một request có nội dung khác.</summary>
    KeyReusedWithDifferentRequest,

    /// <summary>Đã xử lý xong nhưng không giải mã được response lưu (mất khóa mã hóa).</summary>
    ReplayUnavailable
}

public sealed record IdempotencyRequest(Guid UserId, string Key, string RequestHash, string Method, string Path);

public sealed record StoredResponse(int StatusCode, string? ContentType, string? Location, byte[] Body);

public sealed record IdempotencyBeginResult(IdempotencyOutcome Outcome, StoredResponse? Response = null);

/// <summary>
/// Lưu kết quả request theo (user, Idempotency-Key) để request gửi lại (retry, double-click) không bị thực thi 2 lần (C-08).
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyBeginResult> TryBeginAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(Guid userId, string key, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>Xóa key khi request lỗi hệ thống — cho phép client thử lại với cùng key.</summary>
    Task AbandonAsync(Guid userId, string key, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

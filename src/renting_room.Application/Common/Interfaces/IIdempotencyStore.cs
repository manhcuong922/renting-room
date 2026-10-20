namespace renting_room.Application.Common.Interfaces;

public enum IdempotencyOutcome
{
    Started,

    Replay,

    InProgress,

    KeyReusedWithDifferentRequest,

    /// <summary>Đã xử lý xong nhưng không giải mã được response lưu (mất khóa mã hóa).</summary>
    ReplayUnavailable
}

public sealed record IdempotencyRequest(Guid UserId, string Key, string RequestHash, string Method, string Path);

public sealed record StoredResponse(int StatusCode, string? ContentType, string? Location, byte[] Body);

public sealed record IdempotencyBeginResult(IdempotencyOutcome Outcome, StoredResponse? Response = null);

public interface IIdempotencyStore
{
    Task<IdempotencyBeginResult> TryBeginAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(Guid userId, string key, StoredResponse response, CancellationToken cancellationToken);

    Task AbandonAsync(Guid userId, string key, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

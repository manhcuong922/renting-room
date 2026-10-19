using System.Data;
using renting_room.Domain.Common;

namespace renting_room.Application.Common.Interfaces;

/// <summary>
/// Ranh giới lưu dữ liệu của một use case: transaction, lưu mọi thay đổi của các repository một lần, kiểm tra version.
/// Use case quyết định lúc nào lưu; repository chỉ lấy và cất. Application không cần biết tới kiểu của EF Core.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Mở transaction. Đang có transaction (VD import gói nhiều lệnh — PR-UC-11, RT-UC-10) ⇒ <b>tham gia</b> transaction đó:
    /// commit của lệnh con không làm gì, bên ngoài quyết định lưu tất cả hoặc không gì.
    /// </summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Transaction mới với mức cô lập chỉ định (VD đọc nhiều lần trên cùng một snapshot).</summary>
    Task<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lưu nhưng không ném lỗi xung đột version / ràng buộc DB — thao tác hàng loạt biến lỗi của 1 phần tử thành kết quả của phần tử đó.
    /// </summary>
    Task<SaveOutcome> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Lỗi lưu do vi phạm ràng buộc DB / xung đột version (dữ liệu vừa bị người khác đổi).</summary>
    bool IsSaveFailure(Exception exception);

    /// <summary>Optimistic concurrency (C-07): version client gửi lên phải khớp bản đang lưu, nếu không lưu sẽ báo 409.</summary>
    void SetExpectedVersion(AuditableEntity entity, uint version);

    /// <summary><c>SELECT … FOR UPDATE</c> trên 1 hàng — tuần tự hóa các lệnh cùng tác động 1 bản ghi. Phải gọi trong transaction.</summary>
    Task LockForUpdateAsync<TEntity>(Guid id, CancellationToken cancellationToken) where TEntity : Entity;

    /// <summary>
    /// Khóa logic theo người thuê tới hết transaction — tuần tự hóa các lệnh làm thay đổi "người này đang ở đâu"
    /// trên các hợp đồng KHÁC nhau (CT-BR-31), nơi khóa hàng hợp đồng không đủ.
    /// </summary>
    Task LockRentersAsync(IEnumerable<Guid> renterIds, CancellationToken cancellationToken);

    /// <summary>Thao tác hàng loạt (mỗi phần tử 1 transaction): bỏ theo dõi giữa các lần để lỗi phần tử trước không dính sang sau.</summary>
    void ClearTracking();
}

public enum SaveOutcome
{
    Saved,

    /// <summary>Bản ghi đã bị người khác sửa (C-07).</summary>
    Conflict,

    /// <summary>Vi phạm ràng buộc DB.</summary>
    Failed
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

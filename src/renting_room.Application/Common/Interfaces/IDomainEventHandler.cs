using renting_room.Domain.Common;

namespace renting_room.Application.Common.Interfaces;

/// <summary>
/// Xử lý sự kiện nghiệp vụ của một lần lưu — gọi ngay sau khi lưu thành công, trong cùng transaction của lệnh (nếu có).
/// Nhận cả lô sự kiện của lần lưu đó để gộp thành ít câu lệnh.
/// </summary>
public interface IDomainEventHandler
{
    Task HandleAsync(DomainEventBatch batch, CancellationToken cancellationToken);
}

public sealed record DomainEventBatch(Guid OrganizationId, IReadOnlyList<IDomainEvent> Events);

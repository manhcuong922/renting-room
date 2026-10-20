using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public interface IMeterQueries
{
    Task<Meter?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Meter>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<Meter>> ListByRoomAsync(Guid roomId, bool includeRemoved, CancellationToken cancellationToken);

    Task<IReadOnlyList<Meter>> ListByRoomsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Meter>> ListByPropertyAsync(Guid propertyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Meter>> ListActiveByFeeAsync(Guid propertyId, Guid feeTypeId, CancellationToken cancellationToken);

    /// <summary>Công tơ của phòng đang đo tại ngày <paramref name="date"/>.</summary>
    Task<List<Guid>> ListActiveIdsInRoomAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken);

    Task<List<Guid>> ListIdsInRoomAsync(Guid roomId, CancellationToken cancellationToken);

    /// <summary>Phòng (trong <paramref name="roomIds"/>) đang có công tơ hoạt động của khoản thu.</summary>
    Task<HashSet<Guid>> GetMeteredRoomIdsAsync(IReadOnlyCollection<Guid> roomIds, Guid feeTypeId, CancellationToken cancellationToken);

    Task<HashSet<Guid>> GetMeteredRoomIdsInPropertyAsync(Guid propertyId, Guid feeTypeId, CancellationToken cancellationToken);

    Task<bool> HasActiveAsync(Guid roomId, Guid feeTypeId, CancellationToken cancellationToken);

    /// <summary>Khoản điện nước có công tơ của phòng còn đo từ ngày <paramref name="from"/>.</summary>
    Task<List<Guid>> ListFeeIdsInRoomAsync(Guid roomId, DateOnly from, CancellationToken cancellationToken);

    Task<Guid?> GetMeterIdOfReadingAsync(Guid readingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeterRoomRow>> ListRoomMetersAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    /// <summary>Khoản thu của công tơ đang đo tại ngày <paramref name="date"/> ở các phòng.</summary>
    Task<IReadOnlyList<MeterFeeRow>> ListActiveFeesAsync(IReadOnlyCollection<Guid> roomIds, DateOnly date, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeterFeeRow>> ListUnremovedFeesAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    /// <summary>Phòng có công tơ của khoản thu còn đo sau ngày <paramref name="date"/>.</summary>
    Task<bool> HasMeterOnAsync(Guid roomId, Guid feeTypeId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Có chỉ số nào trong <paramref name="readingIds"/> ghi sau ngày <paramref name="date"/>.</summary>
    Task<bool> AnyReadingAfterAsync(IReadOnlyCollection<Guid> readingIds, DateOnly date, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeterLastReadingRow>> ListLastReadingsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<ReadingValue?> GetActiveReadingAsync(Guid readingId, CancellationToken cancellationToken);
}

public sealed record ReadingValue(Guid MeterId, decimal Value);

public sealed record MeterLastReadingRow(Guid RoomId, string? SerialNo, DateOnly? Last);

public sealed record MeterRoomRow(Guid Id, Guid RoomId);

public sealed record MeterFeeRow(Guid RoomId, Guid FeeTypeId);

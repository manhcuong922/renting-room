using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

/// <summary>Lấy / cất aggregate công tơ (kèm chỉ số). Không chứa luật nghiệp vụ — luật nằm trong <see cref="Meter"/>.</summary>
public interface IMeterRepository
{
    /// <summary>Khóa công tơ (sắp theo id — tránh deadlock C-07) rồi nạp kèm chỉ số. Phải gọi trong transaction.</summary>
    Task<List<Meter>> LockAndLoadAsync(IEnumerable<Guid> meterIds, CancellationToken cancellationToken);

    void Add(Meter meter);

    void AddRange(IEnumerable<Meter> meters);
}

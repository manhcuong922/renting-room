using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public interface IRenterRepository
{
    Task<Renter?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tìm trùng số giấy tờ trong <b>cả tổ chức</b> — kể cả hồ sơ ngoài phạm vi khu của quản lý (RT-BR-02 / ID-BR-26).</summary>
    Task<Guid?> FindIdByIdNumberHashAsync(string hash, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>
    /// ID-BR-26 (08/11/2026): quản lý khu nhập đúng số giấy tờ của hồ sơ ngoài phạm vi ⇒ ghi nhận để từ đó thấy / dùng lại hồ sơ (gọi SaveChanges sau).
    /// Trả họ tên + 4 số cuối để báo "đã có hồ sơ".
    /// </summary>
    Task<(string FullName, string? IdNumberLast4)> ClaimAsync(Guid renterId, CancellationToken cancellationToken);

    void Add(Renter renter);
}

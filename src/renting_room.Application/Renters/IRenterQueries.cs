using renting_room.Application.Common.Models;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public interface IRenterQueries
{
    Task<RenterDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>null ⇒ không có hồ sơ này.</summary>
    Task<bool?> IsAnonymizedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>null ⇒ không có hồ sơ này.</summary>
    Task<RenterIdNumber?> GetIdNumberAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<RenterDto>> SearchAsync(RenterSearch search, CancellationToken cancellationToken);

    /// <summary>Mới nhất trước (chỉ đọc).</summary>
    Task<IReadOnlyList<RenterConsent>> ListConsentsAsync(Guid renterId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken);

    /// <summary>Người thuê có họ tên (dạng tìm kiếm không dấu) chứa <paramref name="searchText"/>, sắp theo tên, tối đa <paramref name="max"/>.</summary>
    Task<IReadOnlyDictionary<Guid, string>> SearchNamesAsync(string searchText, int max, CancellationToken cancellationToken);

    /// <summary>Hồ sơ (chỉ đọc).</summary>
    Task<IReadOnlyDictionary<Guid, Renter>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Hồ sơ chưa ẩn danh (chỉ đọc).</summary>
    Task<Renter?> FindActiveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Hồ sơ chưa ẩn danh (RT-BR-06) — người ẩn danh không đứng tên / ở trong HĐ mới được.</summary>
    Task<IReadOnlyList<RenterPerson>> ListPeopleAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>RT-BR-01: hồ sơ có số giấy tờ.</summary>
    Task<bool> HasIdNumberAsync(Guid id, CancellationToken cancellationToken);

    Task<int> CountWithIdNumberAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Người chưa ẩn danh mà chưa có số giấy tờ.</summary>
    Task<IReadOnlyList<RenterPerson>> ListMissingIdNumberAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<RenterContact?> GetContactAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Hồ sơ có số giấy tờ (hash) trong danh sách.</summary>
    Task<IReadOnlyList<RenterIdentity>> ListByIdNumberHashesAsync(IReadOnlyCollection<string?> hashes, CancellationToken cancellationToken);

    /// <summary>Còn HĐ nháp / hiệu lực / thanh lý có người này (đứng tên, ở cùng, hoặc từng đứng tên).</summary>
    Task<bool> HasOpenContractAsync(Guid renterId, CancellationToken cancellationToken);

    /// <summary>Các HĐ người này đứng tên còn phiếu nợ chưa thu / phiếu chờ hoàn.</summary>
    Task<bool> HasUnsettledInvoiceAsync(Guid renterId, CancellationToken cancellationToken);

    /// <summary>Hồ sơ chưa ẩn danh, không vướng 2 điều kiện chặn trên; kèm các mốc để tính lần cuối gắn với HĐ.</summary>
    Task<IReadOnlyList<AnonymizationCandidate>> ListAnonymizationCandidatesAsync(CancellationToken cancellationToken);
}

public sealed record RenterPerson(Guid Id, DateOnly DateOfBirth, Gender Gender, string FullName);

public sealed record RenterIdentity(Guid Id, string? IdNumberHash, string FullName, DateOnly DateOfBirth, string? Phone);

public sealed record RenterContact(string? Phone, DateOnly DateOfBirth, bool IsAnonymized);

public sealed record RenterIdNumber(byte[]? Encrypted, bool IsAnonymized);

/// <param name="IdNumberHashes">null = không lọc theo số giấy tờ.</param>
/// <param name="Text">null = không lọc theo tên / SĐT.</param>
public sealed record RenterSearch(
    bool IncludeArchived, Guid? PropertyId, bool? Staying, DateOnly Today, IReadOnlyList<string?>? IdNumberHashes, string? Text, string? Phone,
    int Page, int PageSize);

/// <param name="LastEnded">Ngày trả phòng muộn nhất của HĐ đã kết thúc.</param>
/// <param name="LastCancelled">Lần hủy HĐ nháp muộn nhất.</param>
public sealed record AnonymizationCandidate(Guid Id, DateTimeOffset CreatedAt, DateOnly? LastEnded, DateTimeOffset? LastCancelled);

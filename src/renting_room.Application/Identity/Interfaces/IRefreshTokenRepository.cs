using renting_room.Domain.Identity;

namespace renting_room.Application.Identity;

/// <summary>Refresh token: thu hồi bằng lệnh UPDATE nguyên tử (không load entity), an toàn khi chạy song song.</summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<Guid?> FindFamilyIdByHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(RefreshToken token);

    Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> RevokeAllForUserAsync(Guid userId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> RevokeAllForOrganizationAsync(Guid organizationId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Xoay vòng: chỉ thành công (trả 1) khi token chưa bị thu hồi — chặn request song song.</summary>
    Task<int> MarkRotatedAsync(Guid id, Guid successorId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

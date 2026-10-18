using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

// Tạo nháp
internal static class ContractNumbers
{
    private const int MaxAttempts = 5;

    /// <summary>Số tự sinh có thể trùng số người dùng đã tự nhập trước đó (VD tự gõ "HD2026-0005") ⇒ lấy số kế tiếp.</summary>
    public static async Task<string> NextAsync(
        IAppDbContext db, IContractNumberGenerator generator, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
    {
        string contractNo;
        var attempt = 0;
        do
        {
            contractNo = await generator.NextAsync(currentUser.OrganizationId!.Value, clock.GetUtcNow().ToBusinessDate().Year, ct);
        }
        while (++attempt < MaxAttempts && await db.Contracts.AnyAsync(c => c.ContractNo == contractNo, ct));
        return contractNo;
    }
}

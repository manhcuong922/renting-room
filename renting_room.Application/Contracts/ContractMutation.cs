using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>
/// Khung chung cho mọi lệnh sửa hợp đồng: transaction + KHÓA HÀNG hợp đồng + nạp đủ aggregate + lưu.
/// Khóa là cần thiết vì thêm người ở / tài sản / xe chỉ ghi bảng con — xmin của hợp đồng không đổi nên
/// optimistic concurrency không phát hiện được 2 request song song (VD 2 người cùng thêm người ở vượt sức chứa).
/// </summary>
internal static class ContractMutation
{
    public static async Task<Result<T>> RunAsync<T>(
        IAppDbContext db, Guid contractId, Func<Contract, Task<Result<T>>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Contract>(contractId, ct);

        var contract = await LoadAsync(db, contractId, ct);
        if (contract is null)
            return ContractErrors.NotFound;

        var result = await action(contract);
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public static async Task<Result> RunAsync(IAppDbContext db, Guid contractId, Func<Contract, Task<Result>> action, CancellationToken ct)
    {
        var result = await RunAsync(db, contractId, async c =>
        {
            var inner = await action(c);
            return inner.IsSuccess ? Result.Success(true) : Result.Failure<bool>(inner.Error!);
        }, ct);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    public static Task<Contract?> LoadAsync(IAppDbContext db, Guid contractId, CancellationToken ct) =>
        db.Contracts
            .Include(c => c.RentTerms)
            .Include(c => c.Occupants)
            .Include(c => c.Assets)
            .Include(c => c.Vehicles)
            .Include(c => c.Fees)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == contractId, ct);
}

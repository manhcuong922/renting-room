using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>
/// Khung chung cho mọi lệnh sửa hợp đồng: transaction + KHÓA HÀNG hợp đồng + nạp đủ aggregate + lưu.
/// Khóa là cần thiết vì thêm người ở / tài sản / xe chỉ ghi bảng con — xmin của hợp đồng không đổi nên
/// optimistic concurrency không phát hiện được 2 request song song (VD 2 người cùng thêm người ở vượt sức chứa).
/// </summary>
public sealed class ContractMutation(IUnitOfWork unitOfWork, IContractRepository contracts)
{
    public async Task<Result<T>> RunAsync<T>(Guid contractId, Func<Contract, Task<Result<T>>> action, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Contract>(contractId, ct);

        var contract = await contracts.GetForUpdateAsync(contractId, ct);
        if (contract is null)
            return ContractErrors.NotFound;

        var result = await action(contract);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Result> RunAsync(Guid contractId, Func<Contract, Task<Result>> action, CancellationToken ct)
    {
        var result = await RunAsync(contractId, async c =>
        {
            var inner = await action(c);
            return inner.IsSuccess ? Result.Success(true) : Result.Failure<bool>(inner.Error!);
        }, ct);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }
}

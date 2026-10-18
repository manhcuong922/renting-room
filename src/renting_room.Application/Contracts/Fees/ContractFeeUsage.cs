using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;

namespace renting_room.Application.Contracts;

// ============================================================ FE-UC-08: phòng đang dùng dịch vụ — thêm / bớt hàng loạt

/// <param name="ContractId">HĐ đang hiệu lực / đang thanh lý của phòng (null = phòng trống ⇒ không thêm được).</param>
/// <param name="IsUsing">HĐ đang đăng ký dịch vụ (hôm nay hoặc đã hẹn từ kỳ sau).</param>
public sealed record FeeUsageRowDto(
    Guid RoomId,
    string RoomCode,
    string? Floor,
    Guid? ContractId,
    string? ContractNo,
    string? RepresentativeName,
    bool IsUsing,
    decimal? Quantity,
    decimal? UnitPriceOverride,
    DateOnly? EffectiveFrom);

public sealed record GetFeeUsageQuery(Guid FeeTypeId) : IRequest<Result<IReadOnlyList<FeeUsageRowDto>>>;

public sealed class GetFeeUsageHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetFeeUsageQuery, Result<IReadOnlyList<FeeUsageRowDto>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeUsageRowDto>>> Handle(GetFeeUsageQuery request, CancellationToken cancellationToken)
    {
        var fee = await db.FeeTypes.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FeeTypeId, cancellationToken);
        if (fee is null)
            return FeeErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.PropertyId == fee.PropertyId && r.ArchivedAt == null)
            .Select(r => new { r.Id, r.Code, r.Floor }).ToListAsync(cancellationToken);
        var contracts = await db.Contracts.AsNoTracking().Include(c => c.Fees)
            .Where(c => c.PropertyId == fee.PropertyId
                && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating)
                && (c.ActualEndDate == null || c.ActualEndDate >= today))
            .ToListAsync(cancellationToken);
        var representativeIds = contracts.Select(c => c.RepresentativeRenterId).ToList();
        var names = await db.Renters.AsNoTracking().Where(r => representativeIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.FullName, cancellationToken);

        var rows = rooms.Select(room =>
            {
                var contract = contracts.Where(c => c.RoomId == room.Id).MinBy(c => c.StartDate);
                // Đang dùng: bản đăng ký hiệu lực hôm nay, hoặc đã hẹn từ kỳ sau (chưa tới ngày).
                var usage = contract?.Fees.Where(f => f.FeeTypeId == fee.Id && (f.Covers(today) || f.EffectiveFrom > today))
                    .MinBy(f => f.EffectiveFrom);
                return new FeeUsageRowDto(room.Id, room.Code, room.Floor, contract?.Id, contract?.ContractNo,
                    contract is null ? null : names.GetValueOrDefault(contract.RepresentativeRenterId), usage is not null,
                    usage?.Quantity, usage?.UnitPriceOverride, usage?.EffectiveFrom);
            })
            .OrderBy(r => r.Floor).ThenBy(r => r.RoomCode)
            .ToList();
        return rows;
    }
}

public enum FeeUsageAction
{
    Add,
    Remove
}

public sealed record FeeUsageResultRow(Guid ContractId, bool Succeeded, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// FE-UC-08: thêm / bớt một dịch vụ cho nhiều HĐ một lúc — mỗi HĐ áp từ kỳ chưa chốt đầu tiên của nó (CT-BR-06, CT-UC-05), transaction riêng,
/// HĐ lỗi không làm hỏng HĐ khác (kết quả từng phòng). <c>Quantity</c> / <c>UnitPriceOverride</c> chỉ dùng khi thêm.
/// </summary>
public sealed record BulkContractFeeCommand(
    Guid FeeTypeId, FeeUsageAction Action, IReadOnlyList<Guid> ContractIds, decimal? Quantity, decimal? UnitPriceOverride)
    : IRequest<Result<IReadOnlyList<FeeUsageResultRow>>>;

public sealed class BulkContractFeeCommandValidator : AbstractValidator<BulkContractFeeCommand>
{
    public BulkContractFeeCommandValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.ContractIds).NotEmpty().WithErrorCode("REQUIRED")
            .Must(ids => ids is null || (ids.Count <= 500 && ids.Distinct().Count() == ids.Count)).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 100).When(x => x.Quantity is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.UnitPriceOverride).OptionalMoney();
    }
}

public sealed class BulkContractFeeHandler(IAppDbContext db, ISender sender)
    : IRequestHandler<BulkContractFeeCommand, Result<IReadOnlyList<FeeUsageResultRow>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeUsageResultRow>>> Handle(BulkContractFeeCommand request, CancellationToken cancellationToken)
    {
        if (!await db.FeeTypes.AnyAsync(f => f.Id == request.FeeTypeId, cancellationToken))
            return FeeErrors.NotFound;

        var results = new List<FeeUsageResultRow>();
        foreach (var contractId in request.ContractIds)
        {
            Result result = request.Action == FeeUsageAction.Add
                ? await sender.Send(new ChangeContractFeeCommand(contractId, request.FeeTypeId, request.Quantity, request.UnitPriceOverride, null),
                    cancellationToken)
                : await sender.Send(new RemoveContractFeeCommand(contractId, request.FeeTypeId, null), cancellationToken);
            results.Add(new FeeUsageResultRow(contractId, result.IsSuccess, result.Error?.Code, result.Error?.Message));
        }
        return results;
    }
}

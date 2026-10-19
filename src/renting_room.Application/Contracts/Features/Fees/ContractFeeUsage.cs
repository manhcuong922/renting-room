using FluentValidation;
using Mediator;
using renting_room.Application.Fees;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
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

public sealed class GetFeeUsageHandler(IFeeQueries fees, IRoomQueries roomQueries, IContractQueries contractQueries, IRenterQueries renters, TimeProvider clock)
    : IRequestHandler<GetFeeUsageQuery, Result<IReadOnlyList<FeeUsageRowDto>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeUsageRowDto>>> Handle(GetFeeUsageQuery request, CancellationToken cancellationToken)
    {
        var fee = await fees.FindTypeAsync(request.FeeTypeId, cancellationToken);
        if (fee is null)
            return FeeErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var rooms = (await roomQueries.ListByPropertyAsync(fee.PropertyId, null, cancellationToken)).Where(r => !r.IsArchived).ToList();
        var contracts = await contractQueries.ListActiveWithFeesAsync(fee.PropertyId, today, cancellationToken);
        var representativeIds = contracts.Select(c => c.RepresentativeRenterId).Distinct().ToList();
        var names = await renters.GetNamesAsync(representativeIds, cancellationToken);

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

public sealed class BulkContractFeeHandler(IFeeQueries fees, ISender sender)
    : IRequestHandler<BulkContractFeeCommand, Result<IReadOnlyList<FeeUsageResultRow>>>
{
    public async ValueTask<Result<IReadOnlyList<FeeUsageResultRow>>> Handle(BulkContractFeeCommand request, CancellationToken cancellationToken)
    {
        if (await fees.FindTypeAsync(request.FeeTypeId, cancellationToken) is null)
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

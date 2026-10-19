using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Application.Fees;
using renting_room.Application.Meters;

namespace renting_room.Application.Contracts;

/// <summary>Dịch vụ gửi kèm HĐ: số gói chỉ cho cách tính <c>PerUnit</c> (null = mặc định của khoản); giá riêng null = theo bảng giá khu.</summary>
public sealed record ContractFeeRequest(Guid FeeTypeId, decimal? Quantity, decimal? UnitPriceOverride);

public sealed record ContractFeeDto(
    Guid Id,
    Guid FeeTypeId,
    string Name,
    FeeGroup Group,
    ChargeBasis? ChargeBasis,
    string Unit,
    decimal Quantity,
    decimal? UnitPriceOverride,
    decimal? CurrentUnitPrice,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

internal static class ContractFeeRules
{
    public const int MaxFees = 30;
    public const decimal MaxOverride = 50_000_000;

    public static bool IsValidQuantity(decimal? q) => q is null || (q > 0 && q <= FeeType.MaxQuantity && decimal.Round(q.Value, 2) == q);

    public static bool IsValidOverride(decimal? p) => p is null || (p >= 0 && p <= MaxOverride && decimal.Round(p.Value, 2) == p);

    /// <summary>
    /// Chuyển yêu cầu thành dữ liệu domain: khoản thu phải thuộc khu của HĐ và chưa ngừng dùng; dịch vụ khác <c>PerUnit</c> luôn số lượng 1.
    /// Điện / nước theo công tơ đi theo công tơ của phòng, không gắn vào HĐ (FE-BR-17).
    /// </summary>
    public static Result<ContractFeeInput> ToInput(FeeType? fee, Guid propertyId, decimal? quantity, decimal? unitPriceOverride)
    {
        if (fee is null || fee.PropertyId != propertyId)
            return FeeErrors.NotInProperty;
        if (fee.IsArchived)
            return FeeErrors.Archived;
        if (fee.Group == FeeGroup.Metered)
            return FeeErrors.MeteredFollowsRoom;
        if (!fee.IsPerUnit && quantity is not null && quantity != 1)
            return Error.Validation("INVALID_QUANTITY", $"\"{fee.Name}\" không tính theo số gói — bỏ trống số lượng.");

        return new ContractFeeInput(fee.Id, fee.IsPerUnit ? quantity ?? fee.AttachQuantity : 1, unitPriceOverride);
    }

    /// <summary>
    /// <paramref name="requested"/> null ⇒ gắn các khoản "tự gắn" của khu (FE-UC-01); danh sách rỗng ⇒ không khoản nào.
    /// </summary>
    public static async Task<Result<IReadOnlyCollection<ContractFeeInput>>> ResolveAsync(
        IFeeQueries feeQueries, Guid propertyId, IReadOnlyList<ContractFeeRequest>? requested, CancellationToken ct)
    {
        if (requested is null)
        {
            var auto = await feeQueries.ListAutoAttachAsync(propertyId, ct);
            return auto.Select(f => new ContractFeeInput(f.Id, f.AttachQuantity, null)).ToList();
        }

        var ids = requested.Select(r => r.FeeTypeId).Distinct().ToList();
        var fees = await feeQueries.GetTypesAsync(ids, ct);
        var inputs = new List<ContractFeeInput>();
        foreach (var r in requested)
        {
            var input = ToInput(fees.GetValueOrDefault(r.FeeTypeId), propertyId, r.Quantity, r.UnitPriceOverride);
            if (input.IsFailure)
                return input.Error!;
            inputs.Add(input.Value!);
        }
        return inputs;
    }

    /// <summary>
    /// Khoản thu gắn với HĐ + điện / nước theo công tơ mà <b>phòng thực có công tơ</b> trong thời gian HĐ (FE-BR-17, M06) —
    /// phòng tính nước theo đầu người không có công tơ nước nên không có dòng nước theo công tơ.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, FeeType>> LoadTypesAsync(
        IFeeQueries fees, IMeterQueries meters, Contract contract, CancellationToken ct)
    {
        var ids = contract.Fees.Select(f => f.FeeTypeId).ToList();
        ids.AddRange(await meters.ListFeeIdsInRoomAsync(contract.RoomId, contract.StartDate, ct));
        ids = ids.Distinct().ToList();
        return await fees.GetTypesWithPricesAsync(ids, ct);
    }

    public static async Task<IReadOnlyList<ContractFeeDto>> ToDtosAsync(
        IFeeQueries fees, IMeterQueries meters, Contract contract, DateOnly today, CancellationToken ct)
    {
        if (contract.Fees.Count == 0)
            return [];
        var types = await LoadTypesAsync(fees, meters, contract, ct);

        return contract.Fees
            .OrderBy(f => types[f.FeeTypeId].SortOrder).ThenBy(f => f.FeeTypeId).ThenBy(f => f.EffectiveFrom)
            .Select(f =>
            {
                var type = types[f.FeeTypeId];
                var priceDate = f.EffectiveFrom > today ? f.EffectiveFrom : today;
                return new ContractFeeDto(f.Id, f.FeeTypeId, type.Name, type.Group, type.ChargeBasis, type.Unit, f.Quantity, f.UnitPriceOverride,
                    f.UnitPriceOverride ?? type.ResolvePrice(priceDate)?.UnitPrice, f.EffectiveFrom, f.EffectiveTo);
            })
            .ToList();
    }
}

// ============================================================ HĐ đang hiệu lực: đổi / gỡ khoản thu từ đầu kỳ

/// <param name="EffectiveFrom">Bỏ trống ⇒ kỳ chưa chốt đầu tiên (CT-UC-05).</param>
public sealed record ChangeContractFeeCommand(Guid ContractId, Guid FeeTypeId, decimal? Quantity, decimal? UnitPriceOverride, DateOnly? EffectiveFrom)
    : IRequest<Result>;

public sealed class ChangeContractFeeCommandValidator : AbstractValidator<ChangeContractFeeCommand>
{
    public ChangeContractFeeCommandValidator()
    {
        RuleFor(x => x.Quantity).Must(ContractFeeRules.IsValidQuantity).WithErrorCode("OUT_OF_RANGE")
            .WithMessage("Số lượng 0–100, tối đa 2 số lẻ.");
        RuleFor(x => x.UnitPriceOverride).Must(ContractFeeRules.IsValidOverride).WithErrorCode("INVALID_AMOUNT")
            .WithMessage("Giá riêng 0–50.000.000đ, tối đa 2 số lẻ.");
    }
}

/// <summary>CT-BR-06: đổi số lượng / giá riêng hoặc gắn thêm khoản thu từ đầu một kỳ chưa chốt phiếu.</summary>
public sealed class ChangeContractFeeHandler(ContractMutation mutation, IFeeQueries fees, IPropertyQueries properties, IInvoiceLockReader invoiceLocks)
    : IRequestHandler<ChangeContractFeeCommand, Result>
{
    public ValueTask<Result> Handle(ChangeContractFeeCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            var fee = await fees.FindTypeAsync(request.FeeTypeId, cancellationToken);
            var input = ContractFeeRules.ToInput(fee, contract.PropertyId, request.Quantity, request.UnitPriceOverride);
            if (input.IsFailure)
                return Result.Failure(input.Error!);

            var firstOpen = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, cancellationToken);
            var schedule = (await properties.GetBillingAsync(contract.PropertyId, cancellationToken)).Schedule;
            return contract.ChangeFee(input.Value!, request.EffectiveFrom ?? firstOpen ?? contract.StartDate, firstOpen, schedule);
        }, cancellationToken));
}

public sealed record RemoveContractFeeCommand(Guid ContractId, Guid FeeTypeId, DateOnly? EffectiveFrom) : IRequest<Result>;

public sealed class RemoveContractFeeHandler(ContractMutation mutation, IPropertyQueries properties, IInvoiceLockReader invoiceLocks)
    : IRequestHandler<RemoveContractFeeCommand, Result>
{
    public ValueTask<Result> Handle(RemoveContractFeeCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId, async contract =>
        {
            var firstOpen = await invoiceLocks.GetFirstOpenPeriodStartAsync(contract.Id, cancellationToken);
            var schedule = (await properties.GetBillingAsync(contract.PropertyId, cancellationToken)).Schedule;
            return contract.RemoveFee(request.FeeTypeId, request.EffectiveFrom ?? firstOpen ?? contract.StartDate, firstOpen, schedule);
        }, cancellationToken));
}

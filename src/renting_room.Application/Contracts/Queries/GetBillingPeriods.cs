using FluentValidation;
using Mediator;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Danh sách kỳ thu (C-05) — để UI chọn ngày áp dụng giá mới, xem lịch thu.</summary>
public sealed record GetBillingPeriodsQuery(Guid ContractId, DateOnly? Until) : IRequest<Result<IReadOnlyList<BillingPeriod>>>;

public sealed class GetBillingPeriodsHandler(IContractQueries contracts, IPropertyQueries properties, TimeProvider clock)
    : IRequestHandler<GetBillingPeriodsQuery, Result<IReadOnlyList<BillingPeriod>>>
{
    private const int DefaultMonthsAhead = 12;

    public async ValueTask<Result<IReadOnlyList<BillingPeriod>>> Handle(GetBillingPeriodsQuery request, CancellationToken cancellationToken)
    {
        var contract = await contracts.GetAsync(request.ContractId, cancellationToken);
        if (contract is null)
            return ContractErrors.NotFound;

        var today = clock.GetUtcNow().ToBusinessDate();
        var until = request.Until
            ?? contract.ActualEndDate
            ?? (contract.EndDate is { } end && end >= today ? end : today.AddMonths(DefaultMonthsAhead));
        var maxUntil = contract.StartDate.AddYears(10);
        if (until > maxUntil)
            until = maxUntil;

        // Chỉ ngày trả phòng thực tế mới cắt kỳ: hết hạn mà chưa thanh lý thì vẫn tiếp tục thu (CT-BR-03).
        var schedule = (await properties.GetBillingAsync(contract.PropertyId, cancellationToken)).Schedule;
        var periods = schedule.ContractPeriods(contract.BillingStartDate, contract.ActualEndDate, until);
        return Result.Success(periods);
    }
}

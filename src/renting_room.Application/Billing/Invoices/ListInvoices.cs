using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

// Truy vấn
public sealed record ListInvoicesQuery(
    Guid? PropertyId, string? BillingMonth, InvoiceStatus? Status, Guid? RoomId, Guid? ContractId, string? Floor,
    bool? UnpaidOnly = null, int Page = 1, int PageSize = Paging.DefaultPageSize, bool? Stale = null) : IRequest<PagedResult<InvoiceSummaryDto>>;

public sealed class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    public ListInvoicesQueryValidator(TimeProvider clock)
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.BillingMonth).Must(v => BillingMonths.Parse(v) is not null).When(x => x.BillingMonth is not null)
            .WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng thu dạng yyyy-MM.");
    }
}

/// <summary>BL-UC-02 / BL-UC-14: danh sách phiếu theo khu / tháng / phòng / HĐ, mới nhất trước.</summary>
public sealed class ListInvoicesHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ListInvoicesQuery, PagedResult<InvoiceSummaryDto>>
{
    public async ValueTask<PagedResult<InvoiceSummaryDto>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = InvoiceFilters.Apply(db, request);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(i => i.PeriodStart).ThenBy(i => i.SnapshotRoomCode)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var today = clock.GetUtcNow().ToBusinessDate();
        return new PagedResult<InvoiceSummaryDto>(items.Select(i => i.ToSummary(today)).ToList(), request.Page, request.PageSize, total);
    }
}

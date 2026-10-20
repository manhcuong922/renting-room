using FluentValidation;
using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record ListInvoicesQuery(
    Guid? PropertyId, string? BillingMonth, InvoiceStatus? Status, Guid? RoomId, Guid? ContractId, string? Floor,
    bool? UnpaidOnly = null, int Page = 1, int PageSize = Paging.DefaultPageSize, bool? Stale = null, bool? Adjusted = null, bool? NeedsResend = null)
    : IRequest<PagedResult<InvoiceSummaryDto>>;

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

public sealed class ListInvoicesHandler(IInvoiceQueries invoices, TimeProvider clock) : IRequestHandler<ListInvoicesQuery, PagedResult<InvoiceSummaryDto>>
{
    public async ValueTask<PagedResult<InvoiceSummaryDto>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var page = await invoices.ListAsync(request, cancellationToken);
        var today = clock.GetUtcNow().ToBusinessDate();
        return new PagedResult<InvoiceSummaryDto>(page.Items.Select(i => i.ToSummary(today)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}

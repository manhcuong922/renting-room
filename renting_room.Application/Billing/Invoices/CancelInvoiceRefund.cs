using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record CancelInvoiceRefundCommand(Guid Id) : IRequest<Result<InvoiceDetailDto>>;

/// <summary>Bỏ xác nhận đã hoàn (nhập nhầm) — phải bỏ trước khi hủy phiếu.</summary>
public sealed class CancelInvoiceRefundHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<CancelInvoiceRefundCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(CancelInvoiceRefundCommand request, CancellationToken cancellationToken) =>
        await RefundMutation.RunAsync(db, request.Id, clock.GetUtcNow().ToBusinessDate(),
            invoice => invoice.CancelRefund(), cancellationToken);
}

using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Application.Billing;

public sealed record FinalizeResult(Guid InvoiceId, bool Success, string? InvoiceNo, string? ErrorCode, string? Message);

public sealed record FinalizeInvoicesBatchCommand(IReadOnlyList<Guid> InvoiceIds) : IRequest<IReadOnlyList<FinalizeResult>>;

public sealed class FinalizeInvoicesBatchCommandValidator : AbstractValidator<FinalizeInvoicesBatchCommand>
{
    public FinalizeInvoicesBatchCommandValidator() =>
        RuleFor(x => x.InvoiceIds).NotEmpty().Must(i => i.Count <= 500).WithErrorCode("OUT_OF_RANGE");
}

/// <summary>BL-UC-07: chốt hàng loạt — mỗi phiếu 1 transaction, trả kết quả từng phiếu.</summary>
public sealed class FinalizeInvoicesBatchHandler(IAppDbContext db, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<FinalizeInvoicesBatchCommand, IReadOnlyList<FinalizeResult>>
{
    public async ValueTask<IReadOnlyList<FinalizeResult>> Handle(FinalizeInvoicesBatchCommand request, CancellationToken cancellationToken)
    {
        var results = new List<FinalizeResult>();
        foreach (var id in request.InvoiceIds.Distinct())
        {
            var result = await FinalizeInvoiceHandler.FinalizeAsync(db, numbers, currentUser, clock, id, cancellationToken);
            results.Add(result.IsSuccess
                ? new FinalizeResult(id, true, result.Value!.Summary.InvoiceNo, null, null)
                : new FinalizeResult(id, false, null, result.Error!.Code, result.Error.Message));
        }
        return results;
    }
}

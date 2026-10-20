using FluentValidation;
using Mediator;
using renting_room.Application.Properties;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

public sealed record PropertyGenerateResult(Guid PropertyId, string PropertyCode, GenerateInvoicesResult? Result, string? ErrorCode);

public sealed record GenerateInvoicesBulkCommand(IReadOnlyList<Guid>? PropertyIds, string BillingMonth, bool RecalculateExistingDrafts)
    : IRequest<IReadOnlyList<PropertyGenerateResult>>;

public sealed class GenerateInvoicesBulkCommandValidator : AbstractValidator<GenerateInvoicesBulkCommand>
{
    public GenerateInvoicesBulkCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.BillingMonth).BillingMonth(clock);
        RuleFor(x => x.PropertyIds).Must(p => p is null || p.Count <= 200).WithErrorCode("OUT_OF_RANGE");
    }
}

public sealed class GenerateInvoicesBulkHandler(IPropertyQueries properties, ISender sender)
    : IRequestHandler<GenerateInvoicesBulkCommand, IReadOnlyList<PropertyGenerateResult>>
{
    public async ValueTask<IReadOnlyList<PropertyGenerateResult>> Handle(GenerateInvoicesBulkCommand request, CancellationToken cancellationToken)
    {
        var codes = await properties.ListCodesAsync(request.PropertyIds, cancellationToken);

        var results = new List<PropertyGenerateResult>();
        foreach (var missing in (request.PropertyIds ?? []).Where(id => codes.All(c => c.Id != id)))
            results.Add(new PropertyGenerateResult(missing, string.Empty, null, PropertyErrors.PropertyNotFound.Code));
        foreach (var property in codes)
        {
            var result = await sender.Send(
                new GenerateInvoicesCommand(property.Id, request.BillingMonth, null, null, request.RecalculateExistingDrafts), cancellationToken);
            results.Add(new PropertyGenerateResult(property.Id, property.Code, result.Value, result.Error?.Code));
        }
        return results;
    }
}

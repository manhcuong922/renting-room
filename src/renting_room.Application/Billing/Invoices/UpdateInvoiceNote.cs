using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

public sealed record UpdateInvoiceNoteCommand(Guid InvoiceId, string? Note) : IRequest<Result<InvoiceDetailDto>>;

public sealed class UpdateInvoiceNoteCommandValidator : AbstractValidator<UpdateInvoiceNoteCommand>
{
    public UpdateInvoiceNoteCommandValidator() => RuleFor(x => x.Note).OptionalText(1000);
}

public sealed class UpdateInvoiceNoteHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateInvoiceNoteCommand, Result<InvoiceDetailDto>>
{
    public async ValueTask<Result<InvoiceDetailDto>> Handle(UpdateInvoiceNoteCommand request, CancellationToken cancellationToken) =>
        await InvoiceAccess.MutateAsync(db, request.InvoiceId, clock.GetUtcNow().ToBusinessDate(), invoice =>
        {
            if (invoice.Status != InvoiceStatus.Draft)
                return Result.Failure(BillingErrors.NotDraft);
            invoice.UpdateNote(request.Note);
            return Result.Success();
        }, cancellationToken);
}

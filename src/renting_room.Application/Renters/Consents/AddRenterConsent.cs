using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// RT-UC-05: ghi nhận đồng ý xử lý dữ liệu cá nhân
public sealed record AddRenterConsentCommand(Guid RenterId, string Purpose, ConsentMethod Method, DateOnly GivenAt, string? Note)
    : IRequest<Result<RenterConsentDto>>;

public sealed class AddRenterConsentCommandValidator : AbstractValidator<AddRenterConsentCommand>
{
    public AddRenterConsentCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Purpose).RequiredText(100, "Mục đích");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.GivenAt).Must(d => d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_DATE").WithMessage("Ngày đồng ý không được ở tương lai.");
        RuleFor(x => x.Note).OptionalText(500);
    }
}

public sealed class AddRenterConsentHandler(IAppDbContext db) : IRequestHandler<AddRenterConsentCommand, Result<RenterConsentDto>>
{
    public async ValueTask<Result<RenterConsentDto>> Handle(AddRenterConsentCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.AsNoTracking().Where(r => r.Id == request.RenterId).Select(r => new { r.AnonymizedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (renter is null)
            return RenterErrors.RenterNotFound;
        if (renter.AnonymizedAt is not null)
            return RenterErrors.Anonymized;

        var consent = RenterConsent.Create(request.RenterId, request.Purpose, request.Method, request.GivenAt, request.Note);
        db.RenterConsents.Add(consent);
        await db.SaveChangesAsync(cancellationToken);
        return consent.ToDto();
    }
}

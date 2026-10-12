using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// ============================================================ RT-UC-05: ghi nhận đồng ý xử lý dữ liệu cá nhân

public sealed record RenterConsentDto(
    Guid Id, Guid RenterId, string Purpose, ConsentMethod Method, DateOnly GivenAt, DateOnly? WithdrawnAt, string? Note, DateTimeOffset CreatedAt);

public sealed record ListRenterConsentsQuery(Guid RenterId) : IRequest<Result<IReadOnlyList<RenterConsentDto>>>;

public sealed record AddRenterConsentCommand(Guid RenterId, string Purpose, ConsentMethod Method, DateOnly GivenAt, string? Note)
    : IRequest<Result<RenterConsentDto>>;

public sealed record WithdrawRenterConsentCommand(Guid RenterId, Guid ConsentId, DateOnly WithdrawnOn) : IRequest<Result>;

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

internal static class RenterConsentMapping
{
    public static RenterConsentDto ToDto(this RenterConsent c) =>
        new(c.Id, c.RenterId, c.Purpose, c.Method, c.GivenAt, c.WithdrawnAt, c.Note, c.CreatedAt);
}

public sealed class ListRenterConsentsHandler(IAppDbContext db) : IRequestHandler<ListRenterConsentsQuery, Result<IReadOnlyList<RenterConsentDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RenterConsentDto>>> Handle(ListRenterConsentsQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Renters.AnyAsync(r => r.Id == request.RenterId, cancellationToken))
            return RenterErrors.RenterNotFound;
        var consents = await db.RenterConsents.AsNoTracking().Where(c => c.RenterId == request.RenterId)
            .OrderByDescending(c => c.GivenAt).ThenByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        return consents.Select(c => c.ToDto()).ToList();
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

public sealed class WithdrawRenterConsentHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<WithdrawRenterConsentCommand, Result>
{
    public async ValueTask<Result> Handle(WithdrawRenterConsentCommand request, CancellationToken cancellationToken)
    {
        var consent = await db.RenterConsents.FirstOrDefaultAsync(c => c.Id == request.ConsentId && c.RenterId == request.RenterId, cancellationToken);
        if (consent is null)
            return Result.Failure(RenterConsentErrors.NotFound);
        if (request.WithdrawnOn > clock.GetUtcNow().ToBusinessDate())
            return Result.Failure(RenterConsentErrors.InvalidWithdrawDate);

        var withdrawn = consent.Withdraw(request.WithdrawnOn);
        if (withdrawn.IsFailure)
            return withdrawn;
        await db.SaveChangesAsync(cancellationToken);
        return withdrawn;
    }
}

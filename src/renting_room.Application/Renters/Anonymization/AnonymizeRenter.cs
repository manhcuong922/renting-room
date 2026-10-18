using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// Ẩn danh tự động (job) và bằng tay
/// <summary>
/// Ẩn danh bằng tay — người thuê yêu cầu xóa dữ liệu, hoặc chủ trọ muốn xóa sớm hơn thời gian giữ. Chỉ chủ trọ; lý do bắt buộc (ghi audit);
/// không cần đủ thời gian giữ nhưng vẫn chặn khi còn HĐ đang chạy / còn nợ / chờ hoàn. Không đảo ngược.
/// </summary>
public sealed record AnonymizeRenterCommand(Guid Id, string Reason) : IRequest<Result>;

public sealed class AnonymizeRenterCommandValidator : AbstractValidator<AnonymizeRenterCommand>
{
    public AnonymizeRenterCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do");
}

public sealed class AnonymizeRenterHandler(IAppDbContext db, RenterAnonymizer anonymizer) : IRequestHandler<AnonymizeRenterCommand, Result>
{
    public async ValueTask<Result> Handle(AnonymizeRenterCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.AsNoTracking().Where(r => r.Id == request.Id)
            .Select(r => new { r.AnonymizedAt }).FirstOrDefaultAsync(cancellationToken);
        if (renter is null)
            return Result.Failure(RenterErrors.RenterNotFound);
        if (renter.AnonymizedAt is not null)
            return Result.Failure(RenterErrors.Anonymized);
        if (await RenterAnonymizer.HasOpenContractAsync(db, request.Id, cancellationToken))
            return Result.Failure(RenterErrors.HasOpenContract);
        if (await RenterAnonymizer.HasUnsettledInvoiceAsync(db, request.Id, cancellationToken))
            return Result.Failure(RenterErrors.HasUnsettledInvoices);

        await anonymizer.AnonymizeAsync(request.Id, request.Reason.Trim(), cancellationToken);
        return Result.Success();
    }
}

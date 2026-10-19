using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Identity;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Payments;

namespace renting_room.Application.Payments;

public sealed record PayAndWriteOffResult(PaymentDto Payment, PaymentDto WriteOff);

public static class WriteOffErrors
{
    public static readonly Error NotAllowed = Error.Forbidden("WRITE_OFF_NOT_ALLOWED",
        "Bạn chưa được chủ trọ cấp quyền bỏ nợ.");
    public static readonly Error NothingLeft = Error.BusinessRule("NOTHING_TO_WRITE_OFF",
        "Số tiền trả đã bằng số còn nợ — dùng \"Đã thu\", không cần bỏ phần còn lại.");
}

/// <summary>Bỏ nợ trên 1 phiếu (F2, PM-BR-16/29): khóa HĐ, kiểm HĐ còn thu được; chủ trọ hoặc phó quản lý được cấp quyền.</summary>
internal static class WriteOffGuard
{
    /// <summary>PM-BR-16: chủ trọ luôn được; phó quản lý chỉ khi chủ trọ bật quyền bỏ nợ cho riêng người đó.</summary>
    public static async Task<bool> IsAllowedAsync(IUserQueries users, ICurrentUser currentUser, CancellationToken ct) =>
        currentUser.Role == UserRole.OrgOwner
        || (currentUser.Role == UserRole.OrgManager && currentUser.UserId is { } userId
            && await users.HasWriteOffPermissionAsync(userId, ct));

    public static async Task<Result<Contract>> LockAsync(
        IUserQueries users, IInvoiceQueries invoices, IContractQueries contracts, IUnitOfWork unitOfWork, ICurrentUser currentUser, Guid invoiceId,
        CancellationToken ct)
    {
        if (!await IsAllowedAsync(users, currentUser, ct))
            return WriteOffErrors.NotAllowed;
        var contractId = await invoices.GetContractIdAsync(invoiceId, ct);
        if (contractId is null)
            return BillingErrors.NotFound;
        await unitOfWork.LockForUpdateAsync<Contract>(contractId.Value, ct);
        var contract = (await contracts.GetAsync(contractId.Value, ct))!;
        return contract.Status is ContractStatus.Active or ContractStatus.Liquidating ? contract : PaymentErrors.ContractNotBillable;
    }
}

/// <param name="Amount">null ⇒ bỏ toàn bộ số còn nợ của phiếu.</param>
public sealed record WriteOffInvoiceCommand(Guid InvoiceId, decimal? Amount, string Reason) : IRequest<Result<PaymentDto>>;

public sealed class WriteOffInvoiceCommandValidator : AbstractValidator<WriteOffInvoiceCommand>
{
    public WriteOffInvoiceCommandValidator()
    {
        RuleFor(x => x.Amount).Must(a => a is null || (a > 0 && decimal.Round(a.Value, 0) == a)).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Reason).RequiredText(500, "Lý do bỏ nợ");
    }
}

/// <summary>PM-UC-16: bỏ nợ (một phần / toàn bộ) phiếu đã chốt bất kỳ lúc nào HĐ còn hiệu lực / đang thanh lý — không tính doanh thu.</summary>
public sealed class WriteOffInvoiceHandler(
    IUserQueries users, IInvoiceQueries invoices, IContractQueries contracts, IUnitOfWork unitOfWork, PaymentPosting posting,
    ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<WriteOffInvoiceCommand, Result<PaymentDto>>
{
    public async ValueTask<Result<PaymentDto>> Handle(WriteOffInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var contract = await WriteOffGuard.LockAsync(users, invoices, contracts, unitOfWork, currentUser, request.InvoiceId, cancellationToken);
        if (contract.IsFailure)
            return contract.Error!;
        var outstanding = await invoices.GetInvoiceOutstandingAsync(request.InvoiceId, cancellationToken);
        if (outstanding is null)
            return BillingErrors.NotFinalized;

        var writeOff = await posting.PostAsync(contract.Value!, request.Amount ?? outstanding,
            PaymentMethod.Cash, clock.GetUtcNow().ToBusinessDate(), request.InvoiceId, null, null, request.Reason, PaymentKind.WriteOff,
            cancellationToken);
        if (writeOff.IsFailure)
            return writeOff.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await PaymentMapping.ToDtosAsync(invoices, [writeOff.Value!], cancellationToken))[0];
    }
}

public sealed record PayAndWriteOffCommand(
    Guid InvoiceId, decimal Amount, PaymentMethod Method, DateOnly PaidAt, string? PayerName, string? Reference, string Reason)
    : IRequest<Result<PayAndWriteOffResult>>;

public sealed class PayAndWriteOffCommandValidator : AbstractValidator<PayAndWriteOffCommand>
{
    public PayAndWriteOffCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Amount).Money(allowZero: false).LessThanOrEqualTo(Payment.MaxAmount).WithErrorCode("INVALID_AMOUNT");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.PaidAt).Must(d => d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_PAID_AT").WithMessage("Ngày thu không được ở tương lai.");
        RuleFor(x => x.PayerName).OptionalText(200);
        RuleFor(x => x.Reference).OptionalText(100);
        RuleFor(x => x.Reason).RequiredText(500, "Lý do bỏ phần còn lại");
    }
}

/// <summary>
/// PM-BR-29: 1 transaction trên 1 phiếu — phiếu thu tiền thật (số người thuê trả) + phiếu bỏ nợ phần còn lại (VD nợ 3tr, trả 2tr, cho 1tr).
/// Trả dần (phần còn lại vẫn là nợ) thì dùng "Đã thu" bình thường.
/// </summary>
public sealed class PayAndWriteOffHandler(
    IUserQueries users, IInvoiceQueries invoices, IContractQueries contracts, IUnitOfWork unitOfWork, PaymentPosting posting,
    ICurrentUser currentUser)
    : IRequestHandler<PayAndWriteOffCommand, Result<PayAndWriteOffResult>>
{
    public async ValueTask<Result<PayAndWriteOffResult>> Handle(PayAndWriteOffCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var contract = await WriteOffGuard.LockAsync(users, invoices, contracts, unitOfWork, currentUser, request.InvoiceId, cancellationToken);
        if (contract.IsFailure)
            return contract.Error!;
        if (request.PaidAt < contract.Value!.StartDate.AddDays(-PaymentPosting.MaxDaysBeforeStart))
            return PaymentErrors.PaidBeforeContract;
        var outstanding = await invoices.GetInvoiceOutstandingAsync(request.InvoiceId, cancellationToken);
        if (outstanding is null)
            return BillingErrors.NotFinalized;
        if (request.Amount > outstanding)
            return PaymentErrors.ExceedsDebt;
        if (request.Amount == outstanding)
            return WriteOffErrors.NothingLeft;

        var paid = await posting.PostAsync(contract.Value!, request.Amount, request.Method, request.PaidAt,
            request.InvoiceId, request.PayerName, request.Reference, null, PaymentKind.Receipt, cancellationToken);
        if (paid.IsFailure)
            return paid.Error!;
        var writeOff = await posting.PostAsync(contract.Value!, outstanding - request.Amount, request.Method,
            request.PaidAt, request.InvoiceId, null, null, request.Reason, PaymentKind.WriteOff, cancellationToken);
        if (writeOff.IsFailure)
            return writeOff.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var dtos = await PaymentMapping.ToDtosAsync(invoices, [paid.Value!, writeOff.Value!], cancellationToken);
        return new PayAndWriteOffResult(dtos[0], dtos[1]);
    }
}

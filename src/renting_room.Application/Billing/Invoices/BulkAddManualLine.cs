using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;

namespace renting_room.Application.Billing;

/// <param name="RoomId">Phòng được chọn mà không có phiếu nháp ⇒ <see cref="InvoiceId"/> null, lỗi <c>NO_DRAFT_INVOICE</c>.</param>
public sealed record BulkManualLineOutcome(Guid? InvoiceId, Guid RoomId, string RoomCode, bool Success, string? ErrorCode, string? Message);

public sealed record BulkManualLineResult(int Added, IReadOnlyList<BulkManualLineOutcome> Results);

/// <summary>
/// Phạm vi: danh sách phiếu, hoặc khu + tháng thu (lọc phòng / tầng) — chỉ phiếu nháp. Một dòng giống nhau thêm vào từng phiếu.
/// </summary>
public sealed record BulkAddManualLineCommand(
    IReadOnlyList<Guid>? InvoiceIds, Guid? PropertyId, string? BillingMonth, IReadOnlyList<Guid>? RoomIds, string? Floor,
    InvoiceLineType Type, string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Note, Guid? FeeTypeId,
    ChargeSettlement? Settlement = null)
    : IRequest<Result<BulkManualLineResult>>;

public sealed class BulkAddManualLineCommandValidator : AbstractValidator<BulkAddManualLineCommand>
{
    public BulkAddManualLineCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x).Must(x => x.InvoiceIds is { Count: > 0 } || (x.PropertyId is not null && x.BillingMonth is not null))
            .WithErrorCode("SCOPE_REQUIRED").WithMessage("Chọn phiếu, hoặc khu + tháng thu.");
        RuleFor(x => x.BillingMonth).BillingMonth(clock).When(x => x.BillingMonth is not null);
        RuleFor(x => x.InvoiceIds).Must(i => i is null || i.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.RoomIds).Must(r => r is null || r.Count <= 1000).WithErrorCode("OUT_OF_RANGE");
        ManualLineRules.Apply(this, x => x.Type, x => x.Description, x => x.Note, x => x.Amount, x => x.Quantity, x => x.UnitPrice);
        // BL-BR-27 (E): hoàn trả gắn phiếu nguồn riêng của từng phòng ⇒ không thêm hàng loạt.
        RuleFor(x => x.Type).NotEqual(InvoiceLineType.Refund)
            .WithErrorCode("REFUND_NOT_BULK").WithMessage("Hoàn trả thêm từng phiếu (chọn phiếu nguồn đã thu) — không thêm hàng loạt.");
    }
}

/// <summary>BL-UC-05: thêm phụ thu / giảm trừ / hoàn trả cho 1 phòng hay nhiều phòng một lúc — mỗi phiếu 1 transaction, trả kết quả từng phiếu.</summary>
public sealed class BulkAddManualLineHandler(
    IUnitOfWork unitOfWork, IInvoiceRepository invoices, IInvoiceQueries invoiceQueries, IRoomQueries rooms, RoomChargeBilling roomCharges,
    TimeProvider clock)
    : IRequestHandler<BulkAddManualLineCommand, Result<BulkManualLineResult>>
{
    public async ValueTask<Result<BulkManualLineResult>> Handle(BulkAddManualLineCommand request, CancellationToken cancellationToken)
    {
        var targets = await invoiceQueries.ListManualLineTargetsAsync(request, cancellationToken);
        var results = new List<BulkManualLineOutcome>();
        foreach (var target in targets.OrderBy(t => t.RoomCode))
        {
            var added = target.Status == InvoiceStatus.Draft
                ? await TryAddOneAsync(target.Id, request, cancellationToken)
                : Result.Failure(BillingErrors.NotDraft);
            results.Add(new BulkManualLineOutcome(target.Id, target.RoomId, target.RoomCode, added.IsSuccess,
                added.Error?.Code, added.Error?.Message));
        }

        if (request.InvoiceIds is not { Count: > 0 } && request.RoomIds is { Count: > 0 } roomIds)
        {
            var covered = targets.Select(t => t.RoomId).ToHashSet();
            var missing = await rooms.ListCodesAsync(roomIds, covered, cancellationToken);
            results.AddRange(missing.Select(r => new BulkManualLineOutcome(null, r.Id, r.Code, false, "NO_DRAFT_INVOICE",
                "Phòng chưa có phiếu nháp tháng này — tạo phiếu trước.")));
        }
        return new BulkManualLineResult(results.Count(r => r.Success), results);
    }

    /// <summary>Lỗi lưu của 1 phiếu (xung đột, ràng buộc DB) thành kết quả của phiếu đó — các phiếu đã lưu trước vẫn giữ.</summary>
    private async Task<Result> TryAddOneAsync(Guid invoiceId, BulkAddManualLineCommand request, CancellationToken ct)
    {
        try
        {
            return await AddOneAsync(invoiceId, request, ct);
        }
        finally
        {
            unitOfWork.ClearTracking();
        }
    }

    private async Task<Result> AddOneAsync(Guid invoiceId, BulkAddManualLineCommand request, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Invoice>(invoiceId, ct);
        var invoice = await invoices.GetForUpdateAsync(invoiceId, ct);
        if (invoice is null)
            return Result.Failure(BillingErrors.NotFound);
        var added = roomCharges.AddManualLine(invoice, request.Type, request.Description, request.Quantity, request.UnitPrice,
            request.Amount, request.Note, request.FeeTypeId, null, request.Settlement, clock.GetUtcNow().ToBusinessDate());
        if (added.IsFailure)
            return Result.Failure(added.Error!);
        switch (await unitOfWork.TrySaveChangesAsync(ct))
        {
            case SaveOutcome.Conflict:
                return Result.Failure(BillingErrors.ConcurrencyConflict);
            case SaveOutcome.Failed:
                return Result.Failure(BillingErrors.SaveFailed);
        }
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
}

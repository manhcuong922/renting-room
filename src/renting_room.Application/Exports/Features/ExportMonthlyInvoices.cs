using System.Data;
using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Exports;

/// <summary>
/// M10 E2 "Thu tiền theo kỳ" (09/11/2026): hóa đơn có tháng thu trong [<paramref name="FromMonth"/>, <paramref name="ToMonth"/>] — đã chốt (kèm nháp khi
/// <paramref name="IncludeDrafts"/>), bỏ hóa đơn đã hủy. <paramref name="PropertyIds"/> trống = mọi khu người dùng được xem.
/// </summary>
public sealed record ExportMonthlyInvoicesQuery(
    IReadOnlyList<Guid>? PropertyIds, string FromMonth, string ToMonth, bool IncludeDrafts = false) : IRequest<Result<ExportFile>>;

public sealed class ExportMonthlyInvoicesQueryValidator : AbstractValidator<ExportMonthlyInvoicesQuery>
{
    public const int MaxMonths = 24;

    public ExportMonthlyInvoicesQueryValidator()
    {
        RuleFor(x => x.PropertyIds).Must(ids => ids is null || ids.Count <= 100).WithErrorCode("OUT_OF_RANGE").WithMessage("Tối đa 100 khu.");
        RuleFor(x => x.FromMonth).Must(v => BillingMonths.Parse(v) is not null).WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng dạng yyyy-MM.");
        RuleFor(x => x.ToMonth).Must(v => BillingMonths.Parse(v) is not null).WithErrorCode("INVALID_BILLING_MONTH").WithMessage("Tháng dạng yyyy-MM.");
        RuleFor(x => x.ToMonth)
            .Must((x, to) => BillingMonths.Parse(x.FromMonth) is not { } from || BillingMonths.Parse(to) is not { } end
                || (end >= from && MonthsBetween(from, end) < MaxMonths))
            .WithErrorCode("INVALID_MONTH_RANGE").WithMessage($"Đến tháng phải ≥ từ tháng và tối đa {MaxMonths} tháng.");
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;
}

public sealed class ExportMonthlyInvoicesHandler(
    IExportQueries exports, IPropertyQueries properties, IUnitOfWork unitOfWork, ISpreadsheetWriter writer, TimeProvider clock)
    : IRequestHandler<ExportMonthlyInvoicesQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(ExportMonthlyInvoicesQuery request, CancellationToken cancellationToken)
    {
        // Khu ngoài tổ chức / ngoài khu được giao của quản lý khu ⇒ 404 cả request (RP-BR-01) — không bỏ qua im lặng.
        if (request.PropertyIds is { Count: > 0 } propertyIds)
        {
            var ids = propertyIds.Distinct().ToList();
            if (await properties.CountAsync(ids, cancellationToken) != ids.Count)
                return PropertyErrors.PropertyNotFound;
        }

        var from = BillingMonths.Parse(request.FromMonth)!.Value;
        var to = BillingMonths.Parse(request.ToMonth)!.Value;
        var now = clock.GetUtcNow();

        await using var snapshot = await unitOfWork.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await exports.CountInvoicesAsync(request, from, to, cancellationToken) > ExportErrors.MaxRows)
            return ExportErrors.TooLarge;
        var data = await exports.LoadInvoicesAsync(request, from, to, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);

        var document = new SpreadsheetDocument(InvoiceExportSheets.Build(data, from, to, now));
        var fileName = $"thu-tien-theo-ky_{now.ToOffset(VietnamTime.Offset):yyyyMMdd-HHmm}.xlsx";
        return new ExportFile(fileName, writer.Write(document));
    }
}

using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Properties;

namespace renting_room.Application.Billing;

// Tạo phiếu nháp hàng loạt
public sealed record PropertyGenerateResult(Guid PropertyId, string PropertyCode, GenerateInvoicesResult? Result, string? ErrorCode);

/// <summary>BL-UC-01: tạo phiếu nháp tháng thu cho nhiều khu trong một lệnh; <paramref name="PropertyIds"/> trống = mọi khu đang dùng.</summary>
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

/// <summary>Mỗi khu chạy như lệnh tạo phiếu của riêng khu đó — khu lỗi (VD không thuộc tổ chức) trả mã lỗi, không làm hỏng khu khác.</summary>
public sealed class GenerateInvoicesBulkHandler(IAppDbContext db, ISender sender)
    : IRequestHandler<GenerateInvoicesBulkCommand, IReadOnlyList<PropertyGenerateResult>>
{
    public async ValueTask<IReadOnlyList<PropertyGenerateResult>> Handle(GenerateInvoicesBulkCommand request, CancellationToken cancellationToken)
    {
        var properties = db.Properties.AsNoTracking();
        properties = request.PropertyIds is { Count: > 0 } ids ? properties.Where(p => ids.Contains(p.Id)) : properties.Where(p => p.ArchivedAt == null);
        var codes = await properties.OrderBy(p => p.Code).Select(p => new { p.Id, p.Code }).ToListAsync(cancellationToken);

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

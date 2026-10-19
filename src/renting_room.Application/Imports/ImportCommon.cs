using renting_room.Application.Common.Interfaces;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Imports;

// PR-UC-11: import phòng từ Excel — xem trước (không giữ) → sửa → lưu dòng hợp lệ
internal static class ImportProperty
{
    public static async Task<Result<Property>> LoadAsync(IPropertyQueries properties, Guid propertyId, CancellationToken ct)
    {
        var property = await properties.GetAsync(propertyId, ct);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        return property.IsArchived ? PropertyErrors.PropertyArchived : property;
    }
}

internal static class ImportPreview
{
    public static ImportPreviewDto<TRow> Of<TRow>(IReadOnlyList<ImportRowResult<TRow>> rows)
    {
        var valid = rows.Count(r => r.IsValid);
        return new ImportPreviewDto<TRow>(valid, rows.Count - valid, rows,
            rows.Count == 0 ? [new ImportIssue("IMPORT_EMPTY", "File không có dòng dữ liệu nào.")] : []);
    }

    /// <summary>Ghi 1 dòng nhật ký <c>Import</c> (số đơn vị đã lưu / bỏ qua) — từng bản ghi tạo ra vẫn có dòng "Created" riêng.</summary>
    public static async Task<Result<ImportSaveResultDto>> RecordAsync(
        IUnitOfWork unitOfWork, IAuditTrail auditTrail, Guid propertyId, string kind, IReadOnlyList<ImportUnitResult> units, CancellationToken ct)
    {
        var saved = units.Count(u => u.Outcome == ImportOutcome.Saved);
        auditTrail.Record(AuditActions.Import, nameof(Property), propertyId, new { kind, saved, skipped = units.Count - saved });
        await unitOfWork.SaveChangesAsync(ct);
        return new ImportSaveResultDto(saved, units.Count - saved, units);
    }
}

internal static class ImportIssues
{
    /// <summary>Lỗi validator của lệnh nghiệp vụ → cột trong file (theo tên thuộc tính).</summary>
    public static IEnumerable<ImportIssue> From(
        FluentValidation.Results.ValidationResult result, IReadOnlyDictionary<string, string> propertyToKey, IReadOnlyDictionary<string, string> headers) =>
        result.Errors.Select(failure =>
        {
            var property = failure.PropertyName.Split('.')[^1];
            var key = propertyToKey.FirstOrDefault(p => p.Key.Equals(property, StringComparison.OrdinalIgnoreCase)).Value;
            return new ImportIssue(failure.ErrorCode ?? "INVALID", failure.ErrorMessage, key is null ? null : headers[key]);
        });
}

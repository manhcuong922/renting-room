using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Exports;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;

namespace renting_room.Application.Imports;

// ============================================================ PR-UC-11: import phòng từ Excel — xem trước (không giữ) → sửa → lưu dòng hợp lệ

/// <param name="ElectricityReading">Chỉ số đầu kỳ hiện tại — công tơ tính là lắp từ ngày chốt kỳ hiện tại của khu.</param>
public sealed record RoomImportRow(
    int RowNumber,
    string? Code,
    string? Floor,
    decimal? AreaM2,
    int? MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyList<string>? Amenities,
    string? Description,
    string? ElectricitySerial,
    decimal? ElectricityReading,
    string? WaterSerial,
    decimal? WaterReading);

internal static class RoomImportTemplate
{
    public static readonly ImportColumn[] Columns =
    [
        new("code", "Mã phòng*", SpreadsheetColumnType.Text, 12),
        new("floor", "Tầng", SpreadsheetColumnType.Text, 8),
        new("area", "Diện tích (m²)", SpreadsheetColumnType.Number, 12),
        new("people", "Số người (loại phòng)", SpreadsheetColumnType.Number, 12),
        new("listedRent", "Giá niêm yết", SpreadsheetColumnType.Money, 14),
        new("deposit", "Tiền cọc mặc định", SpreadsheetColumnType.Money, 14),
        new("amenities", "Tiện nghi", SpreadsheetColumnType.Text, 24),
        new("description", "Ghi chú", SpreadsheetColumnType.Text, 24),
        new("electricitySerial", "Số seri công tơ điện", SpreadsheetColumnType.Text, 16),
        new("electricityReading", "Chỉ số điện đầu kỳ", SpreadsheetColumnType.Number, 14),
        new("waterSerial", "Số seri công tơ nước", SpreadsheetColumnType.Text, 16),
        new("waterReading", "Chỉ số nước đầu kỳ", SpreadsheetColumnType.Number, 14)
    ];

    public static readonly Dictionary<string, string> Headers = Columns.ToDictionary(c => c.Key, c => c.Header.TrimEnd('*'));

    /// <summary>Tên thuộc tính lỗi của validator lệnh tạo phòng → cột trong file.</summary>
    public static readonly Dictionary<string, string> PropertyToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = "code", ["floor"] = "floor", ["areaM2"] = "area", ["maxOccupants"] = "people", ["listedRent"] = "listedRent",
        ["defaultDeposit"] = "deposit", ["amenities"] = "amenities", ["description"] = "description"
    };

    public static RoomSpecInput Spec(RoomImportRow row) =>
        new(row.Floor, row.AreaM2, row.MaxOccupants, row.ListedRent, row.DefaultDeposit, row.Amenities ?? [], row.Description);

    /// <summary>Đọc 1 dòng Excel thành dòng nhập (lỗi định dạng gom vào <paramref name="row"/>).</summary>
    public static RoomImportRow Parse(ImportRow row) => new(
        row.RowNumber,
        row.Text("code") is { } code ? TextNormalizer.NormalizeCode(code) : null,
        row.Text("floor"),
        row.Decimal("area"),
        row.Int("people"),
        row.Decimal("listedRent"),
        row.Decimal("deposit"),
        (row.Text("amenities") ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant()).Distinct().ToList(),
        row.Text("description"),
        row.Text("electricitySerial"),
        row.Decimal("electricityReading"),
        row.Text("waterSerial"),
        row.Decimal("waterReading"));
}

public sealed record GetRoomImportTemplateQuery(Guid PropertyId) : IRequest<Result<ExportFile>>;

public sealed class GetRoomImportTemplateHandler(IAppDbContext db, ISpreadsheetWriter writer)
    : IRequestHandler<GetRoomImportTemplateQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(GetRoomImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var code = await db.Properties.Where(p => p.Id == request.PropertyId).Select(p => p.Code).FirstOrDefaultAsync(cancellationToken);
        if (code is null)
            return PropertyErrors.PropertyNotFound;

        var sheet = new SpreadsheetSheet("Phòng", $"Mẫu nhập phòng — khu {code}",
            "Cột có * là bắt buộc. Tiện nghi: mã cách nhau dấu phẩy (VD air_con, wifi). Chỉ số đầu kỳ = số chốt đầu kỳ hiện tại của khu " +
            "(công tơ tính là lắp từ ngày chốt). Tối đa 500 dòng; không dùng công thức.",
            RoomImportTemplate.Columns.Select(c => new SpreadsheetColumn(c.Header, c.Type, c.Width)).ToList(),
            [["101", "1", 20, 2, 3_000_000, 3_000_000, "air_con, wifi", null, "E-101", 1250, null, 340]]);
        return new ExportFile($"mau-nhap-phong_{code}.xlsx", writer.Write(new SpreadsheetDocument([sheet])));
    }
}

/// <summary>Kiểm các dòng nhập phòng theo dữ liệu hiện tại — dùng chung cho xem trước và lưu (PR-UC-11).</summary>
internal sealed class RoomImportChecker(IAppDbContext db, IValidator<CreateRoomCommand> roomValidator, IValidator<InstallMeterCommand> meterValidator)
{
    public async Task<IReadOnlyList<ImportRowResult<RoomImportRow>>> CheckAsync(
        Property property, IReadOnlyList<RoomImportRow> rows, IReadOnlyDictionary<int, List<ImportIssue>> parseErrors, DateOnly meterDate,
        CancellationToken ct)
    {
        var existing = (await db.Rooms.Where(r => r.PropertyId == property.Id).Select(r => r.Code).ToListAsync(ct)).ToHashSet();
        var meterFees = await MeterFeesAsync(db, property.Id, ct);
        var duplicates = rows.Where(r => r.Code is not null).GroupBy(r => TextNormalizer.NormalizeCode(r.Code!))
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var results = new List<ImportRowResult<RoomImportRow>>();
        foreach (var row in rows)
        {
            var errors = new List<ImportIssue>(parseErrors.GetValueOrDefault(row.RowNumber) ?? []);
            var code = row.Code is null ? null : TextNormalizer.NormalizeCode(row.Code);
            if (code is null)
                errors.Add(new ImportIssue("REQUIRED", "Mã phòng là bắt buộc.", RoomImportTemplate.Headers["code"]));
            else
            {
                var check = await roomValidator.ValidateAsync(new CreateRoomCommand(property.Id, code, RoomImportTemplate.Spec(row)), ct);
                errors.AddRange(ImportIssues.From(check, RoomImportTemplate.PropertyToKey, RoomImportTemplate.Headers));
                if (duplicates.Contains(code))
                    errors.Add(new ImportIssue("DUPLICATE_IN_FILE", $"Mã phòng {code} lặp lại trong file.", RoomImportTemplate.Headers["code"]));
                else if (existing.Contains(code))
                    errors.Add(new ImportIssue("ROOM_CODE_TAKEN", $"Khu đã có phòng {code}.", RoomImportTemplate.Headers["code"]));
            }
            Meter(row.ElectricitySerial, row.ElectricityReading, "electricitySerial", "electricityReading", FeeSystemCodes.Electricity, "điện");
            Meter(row.WaterSerial, row.WaterReading, "waterSerial", "waterReading", FeeSystemCodes.Water, "nước");
            results.Add(new ImportRowResult<RoomImportRow>(row with { Code = code }, errors, []));

            void Meter(string? serial, decimal? reading, string serialKey, string readingKey, string systemCode, string label)
            {
                if (serial is not null && reading is null)
                    errors.Add(new ImportIssue("REQUIRED", $"Có số seri công tơ {label} thì phải có chỉ số đầu kỳ.", RoomImportTemplate.Headers[readingKey]));
                if (reading is null)
                    return;
                if (!meterFees.TryGetValue(systemCode, out var feeTypeId))
                {
                    errors.Add(new ImportIssue("FEE_NOT_FOUND", $"Khu chưa có khoản {label} theo công tơ.", RoomImportTemplate.Headers[readingKey]));
                    return;
                }
                foreach (var failure in meterValidator.Validate(new InstallMeterCommand(Guid.NewGuid(), feeTypeId, serial, meterDate, reading.Value, null)).Errors)
                    errors.Add(new ImportIssue(failure.ErrorCode, failure.ErrorMessage,
                        RoomImportTemplate.Headers[failure.PropertyName == nameof(InstallMeterCommand.SerialNo) ? serialKey : readingKey]));
            }
        }
        return results;
    }

    public static Task<Dictionary<string, Guid>> MeterFeesAsync(IAppDbContext db, Guid propertyId, CancellationToken ct) =>
        db.FeeTypes.Where(f => f.PropertyId == propertyId && f.SystemCode != null && f.ArchivedAt == null)
            .ToDictionaryAsync(f => f.SystemCode!, f => f.Id, ct);

    /// <summary>Công tơ khai qua import tính là lắp từ ngày chốt kỳ hiện tại của khu (chỉ số đầu kỳ chủ trọ vẫn ghi sổ).</summary>
    public static DateOnly MeterDate(Property property, DateOnly today) => property.BillingSchedule.StandardPeriodContaining(today).Start;
}

/// <summary>PR-UC-11 bước 1: đọc file, kiểm từng dòng — <b>không lưu gì, server không giữ gì</b>.</summary>
public sealed record PreviewRoomImportCommand(Guid PropertyId, byte[] Content) : IRequest<Result<ImportPreviewDto<RoomImportRow>>>;

public sealed class PreviewRoomImportHandler(
    IAppDbContext db, ISpreadsheetReader reader, IValidator<CreateRoomCommand> roomValidator, IValidator<InstallMeterCommand> meterValidator,
    TimeProvider clock)
    : IRequestHandler<PreviewRoomImportCommand, Result<ImportPreviewDto<RoomImportRow>>>
{
    public async ValueTask<Result<ImportPreviewDto<RoomImportRow>>> Handle(PreviewRoomImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var table = ImportFiles.Locate(reader, request.Content, RoomImportTemplate.Columns, out var fileError);
        if (table is null)
            return fileError!;

        var rows = table.Rows.Select(RoomImportTemplate.Parse).ToList();
        var parseErrors = table.Rows.ToDictionary(r => r.RowNumber, r => r.Errors);
        var meterDate = RoomImportChecker.MeterDate(property.Value!, clock.GetUtcNow().ToBusinessDate());
        var results = await new RoomImportChecker(db, roomValidator, meterValidator)
            .CheckAsync(property.Value!, rows, parseErrors, meterDate, cancellationToken);
        return ImportPreview.Of(results);
    }
}

/// <summary>PR-UC-11 bước 2: nhận các dòng (đã sửa trên màn), kiểm lại toàn bộ, lưu dòng hợp lệ — mỗi dòng 1 transaction.</summary>
public sealed record SaveRoomImportCommand(Guid PropertyId, IReadOnlyList<RoomImportRow> Rows) : IRequest<Result<ImportSaveResultDto>>;

public sealed class SaveRoomImportCommandValidator : AbstractValidator<SaveRoomImportCommand>
{
    public SaveRoomImportCommandValidator() =>
        RuleFor(x => x.Rows).NotEmpty().WithErrorCode("REQUIRED").Must(r => r is null || r.Count <= ImportErrors.MaxRows).WithErrorCode("IMPORT_TOO_MANY_ROWS");
}

public sealed class SaveRoomImportHandler(
    IAppDbContext db, ISender sender, IAuditTrail auditTrail, IValidator<CreateRoomCommand> roomValidator,
    IValidator<InstallMeterCommand> meterValidator, TimeProvider clock)
    : IRequestHandler<SaveRoomImportCommand, Result<ImportSaveResultDto>>
{
    public async ValueTask<Result<ImportSaveResultDto>> Handle(SaveRoomImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var meterDate = RoomImportChecker.MeterDate(property.Value!, clock.GetUtcNow().ToBusinessDate());
        var checks = await new RoomImportChecker(db, roomValidator, meterValidator)
            .CheckAsync(property.Value!, request.Rows, new Dictionary<int, List<ImportIssue>>(), meterDate, cancellationToken);
        var meterFees = await RoomImportChecker.MeterFeesAsync(db, request.PropertyId, cancellationToken);

        var units = new List<ImportUnitResult>();
        foreach (var check in checks)
        {
            var row = check.Row;
            var key = row.Code ?? $"#{row.RowNumber}";
            if (!check.IsValid)
            {
                units.Add(new ImportUnitResult(key, [row.RowNumber], ImportOutcome.Invalid, check.Errors));
                continue;
            }
            var failure = await ImportFiles.SaveUnitAsync(db, async () =>
            {
                var created = await sender.Send(new CreateRoomCommand(request.PropertyId, row.Code!, RoomImportTemplate.Spec(row)), cancellationToken);
                if (created.IsFailure)
                    return created.Error;
                foreach (var (code, serial, reading) in new[]
                         {
                             (FeeSystemCodes.Electricity, row.ElectricitySerial, row.ElectricityReading),
                             (FeeSystemCodes.Water, row.WaterSerial, row.WaterReading)
                         })
                {
                    if (reading is not { } value)
                        continue;
                    var installed = await sender.Send(
                        new InstallMeterCommand(created.Value, meterFees[code], serial, meterDate, value, "Nhập từ Excel"), cancellationToken);
                    if (installed.IsFailure)
                        return installed.Error;
                }
                return null;
            }, cancellationToken);
            units.Add(new ImportUnitResult(key, [row.RowNumber], failure.Count == 0 ? ImportOutcome.Saved : ImportOutcome.Failed, failure));
        }
        return await ImportPreview.RecordAsync(db, auditTrail, request.PropertyId, "Rooms", units, cancellationToken);
    }
}

internal static class ImportProperty
{
    public static async Task<Result<Property>> LoadAsync(IAppDbContext db, Guid propertyId, CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == propertyId, ct);
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
        IAppDbContext db, IAuditTrail auditTrail, Guid propertyId, string kind, IReadOnlyList<ImportUnitResult> units, CancellationToken ct)
    {
        var saved = units.Count(u => u.Outcome == ImportOutcome.Saved);
        auditTrail.Record(AuditActions.Import, nameof(Property), propertyId, new { kind, saved, skipped = units.Count - saved });
        await db.SaveChangesAsync(ct);
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

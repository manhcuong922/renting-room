using System.Globalization;
using System.Text.RegularExpressions;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;

namespace renting_room.Application.Imports;

// ============================================================ Dùng chung cho import Excel (PR-UC-11, RT-UC-10)

/// <param name="Column">Tiêu đề cột trong file (null = lỗi cả dòng / cả phòng).</param>
public sealed record ImportIssue(string Code, string Message, string? Column = null);

/// <summary>Một dòng đã đọc + lỗi / cảnh báo — UI hiển thị, cho sửa thẳng rồi gửi lại <see cref="Row"/> khi lưu.</summary>
public sealed record ImportRowResult<TRow>(TRow Row, IReadOnlyList<ImportIssue> Errors, IReadOnlyList<ImportIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <param name="FileErrors">Lỗi cả file (sai mẫu, quá số dòng…) — không có dòng nào được đọc.</param>
public sealed record ImportPreviewDto<TRow>(int ValidCount, int ErrorCount, IReadOnlyList<ImportRowResult<TRow>> Rows, IReadOnlyList<ImportIssue> FileErrors);

public enum ImportOutcome
{
    /// <summary>Đã lưu.</summary>
    Saved,
    /// <summary>Không hợp lệ ⇒ bỏ qua, chưa lưu (sửa rồi lưu lại).</summary>
    Invalid,
    /// <summary>Hợp lệ nhưng lỗi lúc lưu (dữ liệu vừa đổi) ⇒ chưa lưu.</summary>
    Failed
}

/// <param name="Key">Mã phòng — đơn vị lưu (form phòng: từng dòng; form người thuê: cả phòng).</param>
public sealed record ImportUnitResult(string Key, IReadOnlyList<int> RowNumbers, ImportOutcome Outcome, IReadOnlyList<ImportIssue> Errors);

public sealed record ImportSaveResultDto(int Saved, int Skipped, IReadOnlyList<ImportUnitResult> Units);

/// <param name="Key">Khóa nội bộ để đọc giá trị.</param>
/// <param name="Header">Tiêu đề cột trong mẫu — "*" = bắt buộc.</param>
internal sealed record ImportColumn(string Key, string Header, SpreadsheetColumnType Type, double Width);

internal static class ImportErrors
{
    public const int MaxRows = 500;

    public static readonly Error FileInvalid = Error.Validation("IMPORT_FILE_INVALID", "Không đọc được file — dùng file .xlsx tải từ mẫu.");
    public static readonly Error FileTooLarge = Error.Validation("IMPORT_FILE_TOO_LARGE", "File quá lớn (tối đa 2 MB, giải nén tối đa 20 MB).");

    public static Error TooManyRows(int count) =>
        Error.Validation("IMPORT_TOO_MANY_ROWS", $"File có {count} dòng — tối đa {MaxRows} dòng mỗi lần.");
}

/// <summary>Bước chung: đọc file + tìm bảng theo mẫu (giới hạn dung lượng, chống zip bomb).</summary>
internal static class ImportFiles
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static ImportTable? Locate(ISpreadsheetReader reader, byte[] content, IReadOnlyList<ImportColumn> columns, out Error? error, string? sheetName = null)
    {
        error = null;
        if (content.Length > MaxBytes)
        {
            error = ImportErrors.FileTooLarge;
            return null;
        }
        IReadOnlyList<SheetData> sheets;
        try
        {
            sheets = reader.Read(content);
        }
        catch (SpreadsheetTooLargeException)
        {
            error = ImportErrors.FileTooLarge;
            return null;
        }
        catch (InvalidDataException)
        {
            error = ImportErrors.FileInvalid;
            return null;
        }
        var candidates = sheetName is null ? sheets : sheets.Where(s => s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)).ToList();
        var table = candidates.Select(s => ImportTable.Locate(s, columns)).FirstOrDefault(t => t is not null);
        if (table is null)
            error = Error.Validation("IMPORT_TEMPLATE_MISMATCH",
                $"Không thấy bảng theo mẫu — cần các cột: {string.Join(", ", columns.Select(c => c.Header))}.");
        else if (table.Rows.Count > ImportErrors.MaxRows)
            error = ImportErrors.TooManyRows(table.Rows.Count);
        return error is null ? table : null;
    }

    /// <summary>Lưu từng đơn vị trong transaction riêng; lỗi ⇒ rollback đơn vị đó và xóa theo dõi để không dính sang đơn vị sau.</summary>
    public static async Task<IReadOnlyList<ImportIssue>> SaveUnitAsync(IAppDbContext db, Func<Task<Error?>> save, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        Error? failure;
        try
        {
            failure = await save();
        }
        catch (FluentValidation.ValidationException ex)
        {
            failure = Error.Validation(ex.Errors.FirstOrDefault()?.ErrorCode ?? "INVALID", ex.Errors.FirstOrDefault()?.ErrorMessage ?? ex.Message);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Dữ liệu vừa đổi (VD ai đó vừa tạo phòng trùng mã / HĐ trùng thời gian) — ràng buộc DB chặn.
            failure = Error.Conflict("IMPORT_UNIT_CONFLICT", "Dữ liệu vừa thay đổi (trùng mã / trùng thời gian ở) — tải lại và kiểm tra.");
        }
        if (failure is null)
        {
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return [];
        }
        await transaction.RollbackAsync(ct);
        db.ChangeTracker.Clear();
        return [new ImportIssue(failure.Code, failure.Message)];
    }
}

/// <summary>Bảng dữ liệu trong 1 sheet: tìm dòng tiêu đề theo mẫu, đọc các dòng sau đó (bỏ dòng trống).</summary>
internal sealed class ImportTable
{
    private const int HeaderSearchRows = 10;

    private ImportTable(IReadOnlyList<ImportRow> rows) => Rows = rows;

    public IReadOnlyList<ImportRow> Rows { get; }

    public static ImportTable? Locate(SheetData sheet, IReadOnlyList<ImportColumn> columns)
    {
        foreach (var header in sheet.Rows.Take(HeaderSearchRows))
        {
            var positions = header.Cells.Select((text, i) => (Name: Normalize(text), i)).Where(x => x.Name.Length > 0)
                .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
            if (!columns.All(c => positions.ContainsKey(Normalize(c.Header))))
                continue;

            var index = columns.ToDictionary(c => c.Key, c => positions[Normalize(c.Header)]);
            var headerByKey = columns.ToDictionary(c => c.Key, c => c.Header.TrimEnd('*', ' '));
            var rows = sheet.Rows.Where(r => r.RowNumber > header.RowNumber && (r.HasFormula || r.Cells.Any(c => c is not null)))
                .Select(r => new ImportRow(r, index, headerByKey))
                .ToList();
            return new ImportTable(rows);
        }
        return null;
    }

    private static string Normalize(string? header) => (header ?? string.Empty).Trim().TrimEnd('*').Trim().ToLowerInvariant();
}

/// <summary>Một dòng dữ liệu: đọc ô theo khóa cột, gom lỗi để trả về bản xem trước.</summary>
internal sealed partial class ImportRow(SheetRow row, IReadOnlyDictionary<string, int> index, IReadOnlyDictionary<string, string> headers)
{
    public int RowNumber => row.RowNumber;
    public List<ImportIssue> Errors { get; } = row.HasFormula
        ? [new ImportIssue("FORMULA_NOT_ALLOWED", "Dòng có ô chứa công thức — chỉ nhập giá trị (RP-BR-02).")]
        : [];
    public List<ImportIssue> Warnings { get; } = [];

    public string Header(string key) => headers[key];

    public string? Text(string key) => index[key] < row.Cells.Count ? row.Cells[index[key]] : null;

    public string? Required(string key)
    {
        var text = Text(key);
        if (text is null)
            Error(key, "REQUIRED", $"{Header(key)} là bắt buộc.");
        return text;
    }

    public decimal? Decimal(string key, bool required = false)
    {
        var text = required ? Required(key) : Text(key);
        if (text is null)
            return null;
        var normalized = text.Replace(" ", string.Empty);
        normalized = ThousandsPattern().IsMatch(normalized) ? normalized.Replace(".", string.Empty).Replace(",", string.Empty) : normalized.Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return value;
        Error(key, "INVALID_NUMBER", $"{Header(key)} không phải số.");
        return null;
    }

    public int? Int(string key, bool required = false)
    {
        var value = Decimal(key, required);
        if (value is null)
            return null;
        if (value == decimal.Truncate(value.Value) && value is >= int.MinValue and <= int.MaxValue)
            return (int)value.Value;
        Error(key, "INVALID_NUMBER", $"{Header(key)} phải là số nguyên.");
        return null;
    }

    public DateOnly? Date(string key, bool required = false)
    {
        var text = required ? Required(key) : Text(key);
        if (text is null)
            return null;
        if (DateOnly.TryParseExact(text, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        Error(key, "INVALID_DATE", $"{Header(key)} không đúng dạng ngày (dd/MM/yyyy).");
        return null;
    }

    public void Error(string? key, string code, string message) => Errors.Add(new ImportIssue(code, message, key is null ? null : Header(key)));

    public void Warn(string? key, string code, string message) => Warnings.Add(new ImportIssue(code, message, key is null ? null : Header(key)));

    [GeneratedRegex(@"^\d{1,3}([.,]\d{3})+$")]
    private static partial Regex ThousandsPattern();
}

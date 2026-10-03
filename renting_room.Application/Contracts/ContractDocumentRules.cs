using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.Results;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Đọc / ghi phần văn bản hợp đồng (điều khoản, định nghĩa trường, giá trị trường) lưu dạng jsonb.</summary>
public static class ContractDocumentJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<ContractClause> Clauses(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<ContractClause>>(json, Json) ?? [];

    public static IReadOnlyList<CustomFieldDefinition> Fields(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<CustomFieldDefinition>>(json, Json) ?? [];

    public static IReadOnlyDictionary<string, JsonElement> Values(string? json) =>
        json is null ? new Dictionary<string, JsonElement>() : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Json) ?? [];

    /// <summary>Chuẩn hóa (trim) và tuần tự hóa; danh sách rỗng ⇒ null.</summary>
    public static string? ToJson(IReadOnlyList<ContractClause>? clauses) =>
        clauses is null || clauses.Count == 0
            ? null
            : JsonSerializer.Serialize(clauses.Select(c => new ContractClause(c.Heading.Trim(), c.Body.Trim())).ToList(), Json);

    public static string? ToJson(IReadOnlyList<CustomFieldDefinition>? fields) =>
        fields is null || fields.Count == 0
            ? null
            : JsonSerializer.Serialize(fields.Select(Normalize).ToList(), Json);

    private static CustomFieldDefinition Normalize(CustomFieldDefinition f) => new(
        f.Key.Trim(),
        f.Label.Trim(),
        f.Type,
        f.Required,
        f.Type == CustomFieldType.Select ? f.Options!.Select(o => o.Trim()).ToList() : null,
        TextNormalizer.TrimToNull(f.Unit),
        TextNormalizer.TrimToNull(f.Hint));
}

/// <summary>
/// Kiểm tra giá trị trường tùy biến theo định nghĩa của mẫu (CT-BR-26) và chuẩn hóa thành JSON lưu trữ.
/// Lỗi ném <see cref="ValidationException"/> để client nhận lỗi theo từng trường (<c>contract.customFields.{key}</c>).
/// </summary>
public static class CustomFieldValues
{
    public const int TextMaxLength = 500;
    public const int LongTextMaxLength = 5000;
    public const decimal NumberLimit = 1_000_000_000_000m;

    public static string? Normalize(
        IReadOnlyList<CustomFieldDefinition> definitions,
        IReadOnlyDictionary<string, JsonElement>? values,
        string pathPrefix)
    {
        values ??= new Dictionary<string, JsonElement>();
        var failures = new List<ValidationFailure>();
        var result = new JsonObject();
        var byKey = definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

        foreach (var key in values.Keys.Where(k => !byKey.ContainsKey(k)))
            failures.Add(Failure(pathPrefix, key, "UNKNOWN_FIELD", "Trường này không có trong mẫu hợp đồng."));

        foreach (var definition in definitions)
        {
            var hasValue = values.TryGetValue(definition.Key, out var value) && !IsEmpty(value);
            if (!hasValue)
            {
                if (definition.Required)
                    failures.Add(Failure(pathPrefix, definition.Key, "REQUIRED", $"{definition.Label} là bắt buộc."));
                continue;
            }

            var node = Convert(definition, value);
            if (node is null)
                failures.Add(Failure(pathPrefix, definition.Key, "INVALID_VALUE", InvalidMessage(definition)));
            else
                result[definition.Key] = node;
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return result.Count == 0 ? null : result.ToJsonString();
    }

    private static bool IsEmpty(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    /// <summary>null ⇒ giá trị sai kiểu / ngoài miền.</summary>
    private static JsonNode? Convert(CustomFieldDefinition definition, JsonElement value) => definition.Type switch
    {
        CustomFieldType.Text => Text(value, TextMaxLength),
        CustomFieldType.LongText => Text(value, LongTextMaxLength),
        CustomFieldType.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                                  && Math.Abs(number) <= NumberLimit
            ? JsonValue.Create(number) : null,
        CustomFieldType.Money => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var money)
                                 && money >= 0 && money <= CommonRules.MaxMoney && decimal.Truncate(money) == money
            ? JsonValue.Create(money) : null,
        CustomFieldType.Date => value.ValueKind == JsonValueKind.String
                                && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null,
        CustomFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? JsonValue.Create(value.GetBoolean()) : null,
        CustomFieldType.Select => value.ValueKind == JsonValueKind.String && definition.Options!.Contains(value.GetString()!.Trim())
            ? JsonValue.Create(value.GetString()!.Trim()) : null,
        _ => null
    };

    private static JsonNode? Text(JsonElement value, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString()!.Trim();
        return text.Length <= maxLength ? JsonValue.Create(text) : null;
    }

    private static string InvalidMessage(CustomFieldDefinition d) => d.Type switch
    {
        CustomFieldType.Text => $"{d.Label}: chuỗi tối đa {TextMaxLength} ký tự.",
        CustomFieldType.LongText => $"{d.Label}: chuỗi tối đa {LongTextMaxLength} ký tự.",
        CustomFieldType.Number => $"{d.Label}: phải là số.",
        CustomFieldType.Money => $"{d.Label}: số tiền nguyên từ 0 đến 1 tỷ.",
        CustomFieldType.Date => $"{d.Label}: ngày dạng yyyy-MM-dd.",
        CustomFieldType.Boolean => $"{d.Label}: true hoặc false.",
        _ => $"{d.Label}: chọn một trong: {string.Join(", ", d.Options ?? [])}."
    };

    private static ValidationFailure Failure(string prefix, string key, string code, string message) =>
        new($"{prefix}.{key}", message) { ErrorCode = code };
}

public sealed class ContractClauseValidator : AbstractValidator<ContractClause>
{
    public ContractClauseValidator()
    {
        RuleFor(x => x.Heading).RequiredText(200, "Tiêu đề điều khoản");
        RuleFor(x => x.Body).RequiredText(10_000, "Nội dung điều khoản");
    }
}

public sealed class CustomFieldDefinitionValidator : AbstractValidator<CustomFieldDefinition>
{
    public const int MaxOptions = 30;

    public CustomFieldDefinitionValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("Mã trường là bắt buộc.")
            .Matches("^[a-z][a-z0-9_]{0,39}$").WithErrorCode("INVALID_FORMAT")
            .WithMessage("Mã trường gồm chữ thường không dấu, số, '_' — bắt đầu bằng chữ, tối đa 40 ký tự (VD: water_price).");
        RuleFor(x => x.Label).RequiredText(100, "Tên hiển thị");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Unit).OptionalText(20);
        RuleFor(x => x.Hint).OptionalText(200);

        When(x => x.Type == CustomFieldType.Select, () =>
            RuleFor(x => x.Options)
                .Must(o => o is { Count: > 0 and <= MaxOptions }
                           && o.All(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 100)
                           && o.Select(v => v.Trim()).Distinct().Count() == o.Count)
                .WithErrorCode("INVALID_OPTIONS")
                .WithMessage($"Trường lựa chọn cần 1–{MaxOptions} lựa chọn, không trùng, mỗi lựa chọn ≤ 100 ký tự."));
        When(x => x.Type != CustomFieldType.Select, () =>
            RuleFor(x => x.Options).Must(o => o is null || o.Count == 0)
                .WithErrorCode("INVALID_OPTIONS").WithMessage("Chỉ trường lựa chọn (Select) mới có danh sách lựa chọn."));
    }
}

/// <summary>Quy tắc dùng chung cho danh sách điều khoản / trường của mẫu và hợp đồng.</summary>
internal static class ContractDocumentRules
{
    public const int MaxClauses = 30;
    public const int MaxFields = 50;

    public static void ClausesRules<T>(this AbstractValidator<T> v, Func<T, IReadOnlyList<ContractClause>?> clauses, string path)
    {
        v.RuleFor(x => clauses(x)).Must(c => c is null || c.Count <= MaxClauses)
            .OverridePropertyName(path).WithErrorCode("OUT_OF_RANGE").WithMessage($"Tối đa {MaxClauses} điều khoản.");
        v.RuleForEach(x => clauses(x) ?? Array.Empty<ContractClause>())
            .SetValidator(new ContractClauseValidator()).OverridePropertyName(path);
    }

    public static void FieldsRules<T>(this AbstractValidator<T> v, Func<T, IReadOnlyList<CustomFieldDefinition>?> fields, string path)
    {
        v.RuleFor(x => fields(x))
            .Must(f => f is null || f.Count <= MaxFields).WithErrorCode("OUT_OF_RANGE").WithMessage($"Tối đa {MaxFields} trường.")
            .Must(f => f is null || f.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == f.Count)
            .WithErrorCode("DUPLICATE_FIELD_KEY").WithMessage("Mã trường bị trùng.")
            .OverridePropertyName(path);
        v.RuleForEach(x => fields(x) ?? Array.Empty<CustomFieldDefinition>())
            .SetValidator(new CustomFieldDefinitionValidator()).OverridePropertyName(path);
    }
}

namespace renting_room.Domain.Common;

/// <summary>Phân loại lỗi nghiệp vụ — tầng API ánh xạ sang HTTP status.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Locked,
    BusinessRule
}

/// <summary>
/// Lỗi nghiệp vụ có mã ổn định (UPPER_SNAKE) để client xử lý theo mã, không theo câu chữ.
/// <see cref="Details"/>: dữ liệu kèm theo cho client (VD id hồ sơ đã có) — trả trong ProblemDetails extensions.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type, IReadOnlyDictionary<string, object?>? Details = null)
{
    public Error WithDetail(string key, object? value) =>
        this with { Details = new Dictionary<string, object?>(Details ?? new Dictionary<string, object?>()) { [key] = value } };

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Locked(string code, string message) => new(code, message, ErrorType.Locked);
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
}

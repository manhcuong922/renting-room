using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using renting_room.Domain.Common;

namespace renting_room.Errors;

/// <summary>
/// Mọi lỗi trả về theo RFC 9457 ProblemDetails + 2 trường mở rộng: <c>code</c> (mã ổn định để client xử lý)
/// và <c>traceId</c> (đối chiếu log khi hỗ trợ khách hàng).
/// </summary>
public static class ProblemResponses
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";

    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            problem.Title ??= TitleFor(status);
            problem.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            problem.Extensions.TryAdd(CodeKey, DefaultCodeFor(status));
            problem.Extensions.TryAdd(TraceIdKey, Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });

    /// <summary>Lỗi nghiệp vụ (Result) → ProblemDetails với HTTP status tương ứng.</summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        var status = StatusFor(error.Type);
        var extensions = new Dictionary<string, object?>(error.Details ?? new Dictionary<string, object?>()) { [CodeKey] = error.Code };
        return TypedResults.Problem(
            title: TitleFor(status),
            detail: error.Message,
            statusCode: status,
            extensions: extensions);
    }

    /// <summary>Ghi ProblemDetails trực tiếp vào response — dùng ở middleware (401, 403, 429).</summary>
    public static async Task WriteAsync(HttpContext httpContext, int status, string code, string detail)
    {
        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(status),
            Detail = detail,
            Extensions = { [CodeKey] = code }
        };

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });

        if (!written)
            await httpContext.Response.WriteAsJsonAsync(problem);
    }

    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Locked => StatusCodes.Status423Locked,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(int status) => status switch
    {
        400 => "Dữ liệu không hợp lệ",
        401 => "Chưa xác thực",
        403 => "Không có quyền truy cập",
        404 => "Không tìm thấy",
        405 => "Phương thức không được hỗ trợ",
        409 => "Xung đột dữ liệu",
        413 => "Dữ liệu gửi lên quá lớn",
        415 => "Định dạng dữ liệu không được hỗ trợ",
        422 => "Vi phạm quy tắc nghiệp vụ",
        423 => "Tài khoản bị khóa",
        429 => "Quá nhiều yêu cầu",
        _ when status >= 500 => "Lỗi hệ thống",
        _ => "Yêu cầu không thành công"
    };

    private static string DefaultCodeFor(int status) => status switch
    {
        400 => "BAD_REQUEST",
        401 => "UNAUTHORIZED",
        403 => "FORBIDDEN",
        404 => "NOT_FOUND",
        405 => "METHOD_NOT_ALLOWED",
        409 => "CONFLICT",
        413 => "PAYLOAD_TOO_LARGE",
        415 => "UNSUPPORTED_MEDIA_TYPE",
        422 => "BUSINESS_RULE_VIOLATION",
        429 => "TOO_MANY_REQUESTS",
        _ when status >= 500 => "INTERNAL_ERROR",
        _ => "ERROR"
    };
}

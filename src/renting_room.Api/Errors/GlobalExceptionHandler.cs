using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using renting_room.Domain.Exceptions;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Errors;

/// <summary>
/// Điểm bắt lỗi duy nhất của API: chuyển exception thành ProblemDetails an toàn.
/// Không bao giờ trả stack trace / câu SQL ra ngoài ở môi trường production.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request {Path} was cancelled by the client", httpContext.Request.Path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => FromValidation(validation),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => Problem(
                StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", "Dữ liệu gửi lên vượt quá dung lượng cho phép."),
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "INVALID_REQUEST",
                "Yêu cầu không đúng định dạng (JSON sai cú pháp, thiếu body hoặc sai kiểu dữ liệu)."),
            DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
            DbUpdateConcurrencyException => Problem(StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
                "Dữ liệu đã bị người khác thay đổi. Vui lòng tải lại và thử lại."),
            DbUpdateException { InnerException: PostgresException postgres } => FromPostgres(postgres),
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR",
                environment.IsDevelopment() ? exception.ToString() : "Đã có lỗi xảy ra. Vui lòng thử lại sau.");
        }
        else
        {
            logger.LogInformation("Request {Method} {Path} failed with {Status} {Code}",
                httpContext.Request.Method, httpContext.Request.Path, problem.Status, problem.Extensions[ProblemResponses.CodeKey]);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails FromValidation(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => ToCamelCasePath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dữ liệu không hợp lệ",
            Detail = "Một hoặc nhiều trường dữ liệu không hợp lệ.",
            Extensions = { [ProblemResponses.CodeKey] = "VALIDATION_FAILED" }
        };
    }

    /// <summary>Lỗi do ràng buộc DB chặn (thường khi 2 request ghi trùng cùng lúc) → 409 với mã nghiệp vụ nếu biết.</summary>
    private ProblemDetails FromPostgres(PostgresException exception)
    {
        switch (exception.SqlState)
        {
            case PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ExclusionViolation
                when DbConstraints.FromViolation(exception.ConstraintName) is { } known:
                return Problem(StatusCodes.Status409Conflict, known.Code, known.Message);
            case PostgresErrorCodes.UniqueViolation:
                return Problem(StatusCodes.Status409Conflict, "DUPLICATE_VALUE", "Dữ liệu đã tồn tại.");
            case PostgresErrorCodes.ExclusionViolation:
                return Problem(StatusCodes.Status409Conflict, "OVERLAP_CONFLICT", "Khoảng thời gian bị chồng lấn.");
            case PostgresErrorCodes.ForeignKeyViolation:
                return Problem(StatusCodes.Status409Conflict, "REFERENCE_CONFLICT",
                    "Dữ liệu đang được tham chiếu hoặc tham chiếu tới dữ liệu không tồn tại.");
            case PostgresErrorCodes.SerializationFailure:
                return Problem(StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
                    "Dữ liệu đang được xử lý đồng thời. Vui lòng thử lại.");
            case PostgresErrorCodes.CheckViolation:
                logger.LogError(exception, "Check constraint {Constraint} violated", exception.ConstraintName);
                return Problem(StatusCodes.Status422UnprocessableEntity, "CONSTRAINT_VIOLATION", "Dữ liệu vi phạm ràng buộc.");
            default:
                logger.LogError(exception, "Unhandled PostgreSQL error {SqlState}", exception.SqlState);
                return Problem(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Đã có lỗi xảy ra. Vui lòng thử lại sau.");
        }
    }

    private static ProblemDetails Problem(int status, string code, string detail) => new()
    {
        Status = status,
        Detail = detail,
        Extensions = { [ProblemResponses.CodeKey] = code }
    };

    /// <summary>"Owner.FullName" → "owner.fullName" để khớp tên field JSON client gửi lên.</summary>
    private static string ToCamelCasePath(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

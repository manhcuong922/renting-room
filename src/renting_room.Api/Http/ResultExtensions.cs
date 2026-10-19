using renting_room.Application.Common.Models;
using renting_room.Domain.Common;
using renting_room.Errors;

namespace renting_room.Http;

/// <summary>Chuyển <see cref="Result"/> của tầng Application thành HTTP response.</summary>
public static class ResultExtensions
{
    /// <summary>Thành công không có dữ liệu → 204; lỗi nghiệp vụ → ProblemDetails.</summary>
    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();

    public static IResult ToCreated(this Result<CreatedWithWarnings> result, string collectionRoute) =>
        result.IsSuccess
            ? Results.Created($"{collectionRoute}/{result.Value!.Id}", result.Value)
            : result.Error!.ToProblem();

    public static IResult ToCreated(this Result<Guid> result, string collectionRoute) =>
        result.IsSuccess
            ? Results.Created($"{collectionRoute}/{result.Value}", new CreatedResponse(result.Value))
            : result.Error!.ToProblem();

    public static string? ClientIp(this HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record CreatedResponse(Guid Id);

using renting_room.Domain.Common;
using renting_room.Errors;

namespace renting_room.Endpoints;

internal static class EndpointHelpers
{
    public const string ApiPrefix = "/api/v1";

    public static string? ClientIp(this HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Response chứa token / mật khẩu tạm / số giấy tờ không được lưu ở cache trình duyệt hay proxy.</summary>
    public static TBuilder WithNoStore<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            return await next(context);
        });

    /// <summary>Thành công không có dữ liệu → 204; lỗi nghiệp vụ → ProblemDetails.</summary>
    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();

    /// <summary>201 + header Location trỏ tới tài nguyên mới.</summary>
    public static IResult ToCreated(this Result<renting_room.Application.Contracts.CreatedWithWarnings> result, string collectionRoute) =>
        result.IsSuccess
            ? Results.Created($"{collectionRoute}/{result.Value!.Id}", result.Value)
            : result.Error!.ToProblem();

    public static IResult ToCreated(this Result<Guid> result, string collectionRoute) =>
        result.IsSuccess
            ? Results.Created($"{collectionRoute}/{result.Value}", new CreatedResponse(result.Value))
            : result.Error!.ToProblem();
}

public sealed record CreatedResponse(Guid Id);

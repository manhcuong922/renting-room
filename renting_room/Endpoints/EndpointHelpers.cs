namespace renting_room.Endpoints;

internal static class EndpointHelpers
{
    public const string ApiPrefix = "/api/v1";

    public static string? ClientIp(this HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Response chứa token / mật khẩu tạm không được lưu ở cache trình duyệt hay proxy.</summary>
    public static TBuilder WithNoStore<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            return await next(context);
        });
}

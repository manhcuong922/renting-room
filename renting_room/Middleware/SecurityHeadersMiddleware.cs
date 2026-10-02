namespace renting_room.Middleware;

/// <summary>Header bảo mật cơ bản cho API (OWASP Secure Headers).</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        // API chỉ trả JSON — cấm mọi nguồn tài nguyên. Không áp cho Swagger UI (cần script/style).
        if (context.Request.Path.StartsWithSegments("/api"))
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

        return next(context);
    }
}

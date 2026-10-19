using Microsoft.AspNetCore.Mvc.Filters;

namespace renting_room.Http;

/// <summary>Response chứa token / mật khẩu tạm / số giấy tờ không được lưu ở cache trình duyệt hay proxy.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoStoreAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
    }
}

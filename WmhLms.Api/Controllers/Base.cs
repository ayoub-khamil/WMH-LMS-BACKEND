using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace WmhLms.Api.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    protected long CurrentUserId
    {
        get
        {
            // JwtSecurityTokenHandler remaps "sub" -> NameIdentifier by default.
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return long.TryParse(raw, out var id) ? id : 0;
        }
    }
    protected bool IsManager => User.IsInRole("manager");
}

public class ErrorHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (Core.Services.ApiException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            ctx.Response.ContentType = "application/json";
            if (ex.StatusCode == 423 && ex.RetryAfterSeconds.HasValue)
                ctx.Response.Headers.RetryAfter = ex.RetryAfterSeconds.Value.ToString();
            await ctx.Response.WriteAsJsonAsync(new { message = ex.Message });
        }
    }
}

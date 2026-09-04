using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Services;

namespace WmhLms.Api.Middleware;

/// <summary>
/// Single place where an exception becomes an HTTP response.
///
/// Every failure is logged; expected failures (ApiException) at Warning with
/// their status, unexpected ones at Error with the full stack. The response
/// body keeps the { message } shape the frontend already reads, and never
/// carries internal detail outside Development.
/// </summary>
public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex)
        {
            logger.LogWarning("{Status} on {Method} {Path}: {Message}",
                ex.StatusCode, ctx.Request.Method, ctx.Request.Path, ex.Message);
            await WriteAsync(ctx, ex.StatusCode, ex.Message, ex.RetryAfterSeconds);
        }
        catch (DbUpdateException ex)
        {
            // The unique indexes on (course, agent) and (agent, quiz item) turn
            // a lost race into a constraint violation rather than a duplicate row.
            logger.LogWarning(ex, "Database constraint violated on {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);
            await WriteAsync(ctx, 409, "That change conflicts with the current state. Please retry.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);
            var message = env.IsDevelopment()
                ? $"Unexpected server error: {ex.Message}"
                : "An unexpected error occurred. Please try again.";
            await WriteAsync(ctx, 500, message);
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, string message,
        int? retryAfterSeconds = null)
    {
        // Once the response has started the status and headers are already on
        // the wire; overwriting them would throw and mask the original error.
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        if (retryAfterSeconds.HasValue)
            ctx.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
        await ctx.Response.WriteAsJsonAsync(new { message });
    }
}

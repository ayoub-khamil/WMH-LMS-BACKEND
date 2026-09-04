namespace WmhLms.Api.Middleware;

/// <summary>
/// Response hardening for a JSON API. The CSP is deliberately restrictive:
/// nothing here should ever be rendered as a document, so denying framing and
/// script execution outright costs nothing and blocks a whole class of
/// content-sniffing and clickjacking tricks.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var headers = ctx.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        headers["Cache-Control"] = "no-store";
        await next(ctx);
    }
}

/// <summary>
/// Gives every request a correlation id, echoes it back, and puts it in the log
/// scope so a report of "it failed at 14:02" can be traced to exact log lines.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var correlationId = ctx.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
                ? incoming.ToString()
                : ctx.TraceIdentifier;

        ctx.Items[HeaderName] = correlationId;
        ctx.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = ctx.Request.Path.Value ?? string.Empty
        }))
        {
            await next(ctx);
        }
    }
}

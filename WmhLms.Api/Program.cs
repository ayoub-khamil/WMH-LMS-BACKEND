using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using WmhLms.Api.Middleware;
using WmhLms.Api.Services;
using WmhLms.Core.Options;
using WmhLms.Core.Seed;
using WmhLms.Core.Services;
using WmhLms.Data;

namespace WmhLms.Api;

public static class Program
{
    // HS256 needs a key of at least 256 bits; anything shorter is rejected by
    // the token handler at runtime, which is a confusing place to find out.
    private const int MinJwtKeyBytes = 32;

    public const string LoginRateLimitPolicy = "login";

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var isDevelopment = builder.Environment.IsDevelopment();

        // Binding comes from configuration (ASPNETCORE_URLS / Kestrel section) so
        // the same build runs behind a proxy, in a container, or locally. The
        // localhost default only applies in Development.
        if (isDevelopment && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            builder.WebHost.UseUrls("http://localhost:5000");

        builder.Services.AddControllers().AddJsonOptions(o =>
        {
            // Frontend contract is snake_case. .NET 8 ships this policy, so the
            // hand-rolled converter this replaced is gone.
            o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        });

        var conn = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Set the ConnectionStrings__Default "
                + "environment variable to the PostgreSQL connection string.");
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordService, PasswordService>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<CourseService>();
        builder.Services.AddScoped<AssignmentService>();
        builder.Services.AddScoped<LearnService>();
        builder.Services.AddScoped<AuditService>();

        builder.Services.Configure<RootManagerOptions>(
            builder.Configuration.GetSection(RootManagerOptions.SectionName));
        builder.Services.AddScoped<RootManagerBootstrap>();

        // Behind a load balancer or ingress the scheme and client IP arrive in
        // headers; without this, HTTPS redirection and rate limiting both see
        // the proxy instead of the caller.
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });

        var jwt = new JwtOptions(
            ResolveJwtKey(builder.Configuration, isDevelopment),
            builder.Configuration["Jwt:Issuer"],
            builder.Configuration["Jwt:Audience"],
            builder.Configuration.GetValue("Jwt:ExpiryMinutes", 1440));
        // Signing and validation both read this one instance.
        builder.Services.AddSingleton(jwt);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                // A signed token alone is not enough: the account behind it must
                // still exist and still be active. Without this, disabling or
                // deleting a user leaves their issued token working until it
                // expires.
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var raw = ctx.Principal?.FindFirst(
                            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                        if (!long.TryParse(raw, out var userId))
                        {
                            ctx.Fail("Token is missing a usable subject.");
                            return;
                        }
                        var account = await db.Users
                            .AsNoTracking()
                            .Where(u => u.Id == userId)
                            .Select(u => new { u.Status, u.Role })
                            .FirstOrDefaultAsync();
                        if (account is null) ctx.Fail("Account no longer exists.");
                        else if (account.Status == "disabled") ctx.Fail("Account is disabled.");
                        else if (!ctx.Principal!.IsInRole(account.Role))
                        {
                            // Role changed since the token was issued.
                            ctx.Fail("Account permissions have changed. Sign in again.");
                        }
                    }
                };
            });

        builder.Services.AddAuthorization(o =>
        {
            // Endpoints are authenticated unless they opt out with [AllowAnonymous].
            // Without this, a controller that simply forgets [Authorize] is public.
            o.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        // Credential stuffing protection. Keyed on client IP so one attacker
        // cannot lock every account out by guessing against a shared bucket.
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = builder.Configuration.GetValue("RateLimit:LoginAttempts", 10),
                        Window = TimeSpan.FromMinutes(
                            builder.Configuration.GetValue("RateLimit:WindowMinutes", 1)),
                        QueueLimit = 0
                    }));
            o.OnRejected = async (ctx, token) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                ctx.HttpContext.Response.ContentType = "application/json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Too many attempts. Please wait a minute and try again." },
                    cancellationToken: token);
            };
        });

        var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
        if (corsOrigins is null || corsOrigins.Length == 0)
        {
            if (!isDevelopment)
                throw new InvalidOperationException(
                    "Cors:Origins must list the allowed frontend origins outside Development.");
            corsOrigins = ["http://localhost:5173"];
        }
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(corsOrigins)
            .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            // Without this the browser hides the correlation id from the SPA
            // and the error banner can never show a reference.
            .WithExposedHeaders("X-Correlation-Id", "Retry-After")));

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        if (!isDevelopment)
            builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

        var app = builder.Build();

        app.UseForwardedHeaders();
        if (!isDevelopment)
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        // Liveness answers "is the process up"; readiness also proves the
        // database is reachable, which is what an orchestrator should gate on.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        }).AllowAnonymous();

        await InitialiseDatabaseAsync(app);
        await app.RunAsync();
    }

    /// <summary>
    /// Production must supply Jwt:Key (env var JWT__KEY or a secret store).
    /// Development falls back to an ephemeral per-run key so no signing secret
    /// ever has to live in a tracked config file.
    /// </summary>
    private static string ResolveJwtKey(IConfiguration config, bool isDevelopment)
    {
        var key = config["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (Encoding.UTF8.GetByteCount(key) < MinJwtKeyBytes)
                throw new InvalidOperationException(
                    $"Jwt:Key must be at least {MinJwtKeyBytes} bytes for HS256 signing.");
            return key;
        }

        if (!isDevelopment)
            throw new InvalidOperationException(
                "Jwt:Key is not configured. Set the JWT__KEY environment variable "
                + "(at least 32 bytes) before starting the API outside Development.");

        // Ephemeral: restarting the API invalidates existing dev tokens, which is
        // the correct trade for never committing a signing key.
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    }

    private static async Task InitialiseDatabaseAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex) when (Describes(ex, "already exists"))
        {
            // The database predates migrations (it was created by EnsureCreated).
            throw new InvalidOperationException(
                "The existing database was not created by migrations. Stop the API, run "
                + "`docker compose down -v` to drop the local database, and start again to rebuild and re-seed it.", ex);
        }

        // Always on, in every environment: without it a fresh production database
        // has no account that can sign in. The app refuses to start if it throws.
        await scope.ServiceProvider.GetRequiredService<RootManagerBootstrap>()
            .RunAsync(CancellationToken.None);

        var seedEnabled = app.Configuration.GetValue("Seed:DemoContent", app.Environment.IsDevelopment());
        if (seedEnabled && !app.Environment.IsDevelopment())
            logger.LogWarning("Demo seeding is enabled outside Development. This creates known accounts.");
        if (seedEnabled)
        {
            await DemoSeed.RunAsync(db,
                scope.ServiceProvider.GetRequiredService<IPasswordService>());
            logger.LogInformation("Demo seed check complete.");
        }
    }

    private static bool Describes(Exception ex, string fragment)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WmhLms.Core.Options;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Seed;

/// <summary>
/// Creates the root manager from configuration on every boot, after the
/// migrations have run, so a fresh database in any environment has an account
/// that can sign in. Idempotent: it does nothing once the account exists.
/// </summary>
public sealed class RootManagerBootstrap(
    AppDbContext db, IPasswordService passwords,
    IOptions<RootManagerOptions> options, TimeProvider clock,
    ILogger<RootManagerBootstrap> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Email) || string.IsNullOrWhiteSpace(o.Password))
            throw new InvalidOperationException(
                "RootManager:Email and RootManager:Password must be configured.");

        var email = Guard.Email(o.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return;

        db.Users.Add(new User
        {
            FirstName = o.FirstName,
            LastName = o.LastName,
            Email = email,
            PasswordHash = passwords.Hash(Guard.Password(o.Password)),
            Role = "manager",
            Status = "active",
            IsRoot = true,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Root manager {Email} created from configuration.", email);
    }
}

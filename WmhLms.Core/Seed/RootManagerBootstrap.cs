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

        // The database permits exactly one root account (8.5). If one already
        // exists under a different address, inserting a second violates the
        // index and kills the process during startup - so a deploy that merely
        // corrects a typo in RootManager:Email would be an outage. Say plainly
        // what was found and carry on with the root that is already there.
        var existingRoot = await db.Users.Where(u => u.IsRoot)
            .Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (existingRoot is not null)
        {
            logger.LogWarning(
                "RootManager:Email is configured as {Configured}, but {Existing} is already the "
                + "root account. Only one root account can exist, so none was created. Sign in as "
                + "{Existing}, or change that account's email, to move the root elsewhere.",
                email, existingRoot, existingRoot);
            return;
        }

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

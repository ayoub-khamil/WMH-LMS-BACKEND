using System.Collections.Concurrent;

namespace WmhLms.Core.Services;

/// <summary>
/// Counts failed sign-ins per account, alongside the per-IP rate limiter.
///
/// The IP limiter stops one noisy source. It does nothing for an office where
/// every agent shares one address: the shared bucket is spent by ordinary
/// typos, so raising its limit is the only way to keep the office working, and
/// raising it is exactly what weakens the protection of any single account.
/// Counting per email instead means ten wrong passwords against one account
/// close that account for the rest of the window and leave everyone else's
/// sign-in untouched.
///
/// State is in memory on purpose: one process serves the app, and a restart
/// clearing the counters is a better trade than a database write on every
/// failed password.
/// </summary>
public sealed class FailedLoginTracker(TimeProvider time)
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _failures = new();

    /// <summary>
    /// Throws 429 once the account has spent its failures. Called before the
    /// password is verified, so a locked-out account costs no hashing work.
    /// </summary>
    public void EnsureNotBlocked(string email)
    {
        if (!_failures.TryGetValue(KeyOf(email), out var entry)) return;
        var elapsed = time.GetUtcNow() - entry.WindowStart;
        if (elapsed >= Window || entry.Count < MaxFailures) return;
        throw ApiException.TooManyRequests(
            "Too many failed sign-in attempts for this account. Please try again later.",
            Math.Max(1, (int)Math.Ceiling((Window - elapsed).TotalSeconds)));
    }

    public void RecordFailure(string email)
    {
        var now = time.GetUtcNow();
        _failures.AddOrUpdate(KeyOf(email),
            _ => (1, now),
            // A failure after the window closes starts a fresh one rather than
            // adding to a count from an hour ago.
            (_, entry) => now - entry.WindowStart >= Window ? (1, now) : (entry.Count + 1, entry.WindowStart));
    }

    /// <summary>Proving the password clears the count: the owner is back.</summary>
    public void Clear(string email) => _failures.TryRemove(KeyOf(email), out _);

    // Emails are stored lower-case (Guard.Email), and the key must match
    // however the caller typed it.
    private static string KeyOf(string? email) => (email ?? "").Trim().ToLowerInvariant();
}

using Microsoft.Extensions.Time.Testing;
using WmhLms.Api.Services;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Tests;

/// <summary>
/// Sign-in and the per-account lockout. The per-IP limiter lives in the request
/// pipeline and is not reachable from here; what is pinned below is the rule
/// that still protects one account when a whole office shares one address.
/// </summary>
public class AuthServiceTests : IDisposable
{
    private const string Email = "agent@x.com";
    private const string Password = "correct-horse-battery";

    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        _db = TestDb.Create();
        var passwords = new PasswordService();
        _db.Users.Add(new User
        {
            Id = 1, FirstName = "F", LastName = "L", Email = Email,
            Role = "agent", Status = "active", PasswordHash = passwords.Hash(Password)
        });
        _db.SaveChanges();

        var tokens = new TokenService(new JwtOptions(new string('k', 48), "wmh", "wmh", 60), _clock);
        _auth = new AuthService(_db, passwords, tokens, new FailedLoginTracker(_clock));
    }

    private async Task<int> RefusedStatus(string email = Email, string password = "wrong-password")
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => _auth.LoginAsync(email, password));
        return ex.StatusCode;
    }

    private async Task SpendTheAllowance(string email = Email)
    {
        for (var i = 0; i < FailedLoginTracker.MaxFailures; i++)
            Assert.Equal(401, await RefusedStatus(email));
    }

    [Fact]
    public async Task The_eleventh_wrong_password_for_one_account_is_refused_with_429()
    {
        await SpendTheAllowance();

        var ex = await Assert.ThrowsAsync<ApiException>(() => _auth.LoginAsync(Email, "wrong-password"));
        Assert.Equal(429, ex.StatusCode);
        // Retry-After is what tells the caller when to come back; without it a
        // 429 is just a closed door.
        Assert.Equal((int)FailedLoginTracker.Window.TotalSeconds, ex.RetryAfterSeconds);
    }

    [Fact]
    public async Task A_locked_account_is_refused_even_with_the_right_password()
    {
        await SpendTheAllowance();

        Assert.Equal(429, await RefusedStatus(password: Password));
    }

    [Fact]
    public async Task Signing_in_clears_the_count()
    {
        for (var i = 0; i < FailedLoginTracker.MaxFailures - 1; i++)
            Assert.Equal(401, await RefusedStatus());

        var (token, user) = await _auth.LoginAsync(Email, Password);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(Email, user.Email);

        // A full allowance again, not one typo away from a lockout.
        await SpendTheAllowance();
    }

    [Fact]
    public async Task The_lockout_lifts_when_the_window_closes()
    {
        await SpendTheAllowance();
        Assert.Equal(429, await RefusedStatus());

        _clock.Advance(FailedLoginTracker.Window);

        Assert.Equal(401, await RefusedStatus());
        var (token, _) = await _auth.LoginAsync(Email, Password);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Locking_one_account_does_not_lock_another()
    {
        // Guessing against an unknown address must not shut the office out.
        await SpendTheAllowance("someone-else@x.com");
        Assert.Equal(429, await RefusedStatus("someone-else@x.com"));

        var (token, _) = await _auth.LoginAsync(Email, Password);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    // ── Timing ──

    /// <summary>Counts password checks; hashing itself is delegated.</summary>
    private sealed class CountingPasswords : IPasswordService
    {
        private readonly PasswordService _inner = new();
        public int Verifications { get; private set; }
        public string Hash(string password) => _inner.Hash(password);
        public bool Verify(User user, string password)
        {
            Verifications++;
            return _inner.Verify(user, password);
        }
    }

    [Fact]
    public async Task An_unknown_email_still_costs_a_password_check()
    {
        // Skipping the hash for unknown emails made them answer faster than
        // real accounts, which gave away which addresses exist.
        var passwords = new CountingPasswords();
        var tokens = new TokenService(new JwtOptions(new string('k', 48), "wmh", "wmh", 60), _clock);
        var auth = new AuthService(_db, passwords, tokens, new FailedLoginTracker(_clock));

        var ex = await Assert.ThrowsAsync<ApiException>(() => auth.LoginAsync("nobody@x.com", "whatever-pass"));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(1, passwords.Verifications);
    }

    public void Dispose() => _db.Dispose();
}

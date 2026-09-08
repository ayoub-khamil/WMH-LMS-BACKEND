using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WmhLms.Core.Options;
using WmhLms.Core.Seed;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Tests;

/// <summary>
/// Boot-time creation of the root account. These run on every start in every
/// environment, so a throw here is a dead application, not a failed request.
/// </summary>
public class RootManagerBootstrapTests : IDisposable
{
    private readonly AppDbContext _db = TestDb.Create();
    private readonly PasswordService _passwords = new();

    private RootManagerBootstrap Bootstrap(string email, string password = "bootstrap-password") =>
        new(_db, _passwords,
            Options.Create(new RootManagerOptions
            {
                Email = email, Password = password, FirstName = "Root", LastName = "Manager"
            }),
            TimeProvider.System,
            NullLogger<RootManagerBootstrap>.Instance);

    private void SeedRoot(string email) =>
        _db.Users.Add(new User
        {
            FirstName = "Existing", LastName = "Root", Email = email,
            Role = "manager", Status = "active", IsRoot = true,
            PasswordHash = _passwords.Hash("existing-password")
        });

    [Fact]
    public async Task An_empty_database_gets_the_configured_root()
    {
        await Bootstrap("root@example.com").RunAsync(CancellationToken.None);

        var user = await _db.Users.SingleAsync();
        Assert.Equal("root@example.com", user.Email);
        Assert.True(user.IsRoot);
        Assert.Equal("manager", user.Role);
    }

    [Fact]
    public async Task Running_twice_creates_one_account()
    {
        await Bootstrap("root@example.com").RunAsync(CancellationToken.None);
        await Bootstrap("root@example.com").RunAsync(CancellationToken.None);

        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task A_root_under_a_different_email_is_left_alone_rather_than_duplicated()
    {
        // The single-root index would reject the insert, and the exception
        // would escape startup. Booting is more important than the rename.
        SeedRoot("original.root@example.com");
        await _db.SaveChangesAsync();

        await Bootstrap("someone.else@example.com").RunAsync(CancellationToken.None);

        var user = await _db.Users.SingleAsync();
        Assert.Equal("original.root@example.com", user.Email);
        Assert.True(user.IsRoot);
    }

    [Fact]
    public async Task A_missing_email_or_password_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Bootstrap("", "a-password").RunAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Bootstrap("root@example.com", "").RunAsync(CancellationToken.None));
    }

    public void Dispose() => _db.Dispose();
}

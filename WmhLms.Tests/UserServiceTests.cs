using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Tests;

/// <summary>
/// Who may administer whom. These rules are the difference between a manager
/// account and an admin console, so each one is pinned.
/// </summary>
public class UserServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UserService _users;
    private readonly AuditService _audit;

    private const long RootId = 1;
    private const long ManagerId = 2;
    private const long OtherManagerId = 3;
    private const long AgentId = 4;

    public UserServiceTests()
    {
        _db = TestDb.Create();
        var passwords = new PasswordService();
        _audit = new AuditService(_db, TimeProvider.System);
        _users = new UserService(_db, passwords, _audit);

        _db.Users.AddRange(
            New(RootId, "root@x.com", "manager", isRoot: true),
            New(ManagerId, "manager@x.com", "manager"),
            New(OtherManagerId, "other@x.com", "manager"),
            New(AgentId, "agent@x.com", "agent"));
        _db.SaveChanges();

        User New(long id, string email, string role, bool isRoot = false) => new()
        {
            Id = id, FirstName = "F", LastName = "L", Email = email,
            Role = role, Status = "active", IsRoot = isRoot,
            PasswordHash = passwords.Hash("initial-password")
        };
    }

    private static async Task<int> StatusOf(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<ApiException>(act);
        return ex.StatusCode;
    }

    // ── Root protection ──

    [Fact]
    public async Task The_root_account_cannot_be_edited() =>
        Assert.Equal(403, await StatusOf(() =>
            _users.UpdateAsync(ManagerId, RootId, new UpdateUserRequest("X", null, null, null, null))));

    [Fact]
    public async Task The_root_account_cannot_be_deleted() =>
        Assert.Equal(403, await StatusOf(() => _users.DeleteAsync(ManagerId, RootId)));

    [Fact]
    public async Task The_root_account_cannot_be_disabled() =>
        Assert.Equal(403, await StatusOf(() => _users.UpdateStatusAsync(ManagerId, RootId, "disabled")));

    // ── Manager peers ──

    [Fact]
    public async Task A_manager_cannot_reset_another_manager_password() =>
        Assert.Equal(403, await StatusOf(() =>
            _users.UpdateAsync(ManagerId, OtherManagerId,
                new UpdateUserRequest(null, null, null, "hijacked-password", null))));

    [Fact]
    public async Task A_manager_cannot_disable_another_manager() =>
        Assert.Equal(403, await StatusOf(() =>
            _users.UpdateStatusAsync(ManagerId, OtherManagerId, "disabled")));

    [Fact]
    public async Task A_manager_cannot_delete_another_manager() =>
        Assert.Equal(403, await StatusOf(() => _users.DeleteAsync(ManagerId, OtherManagerId)));

    [Fact]
    public async Task A_manager_cannot_create_a_manager() =>
        Assert.Equal(403, await StatusOf(() =>
            _users.CreateAsync(ManagerId, new CreateUserRequest("A", "B", "new@x.com", "a-good-password", "manager"))));

    [Fact]
    public async Task Root_can_administer_a_manager()
    {
        var updated = await _users.UpdateAsync(RootId, OtherManagerId,
            new UpdateUserRequest("Renamed", null, null, null, null));
        Assert.Equal("Renamed", updated.FirstName);
    }

    [Fact]
    public async Task A_manager_can_administer_an_agent()
    {
        var updated = await _users.UpdateAsync(ManagerId, AgentId,
            new UpdateUserRequest("Renamed", null, null, null, null));
        Assert.Equal("Renamed", updated.FirstName);
    }

    // ── Self protection ──

    [Fact]
    public async Task Nobody_can_disable_their_own_account() =>
        Assert.Equal(403, await StatusOf(() => _users.UpdateStatusAsync(ManagerId, ManagerId, "disabled")));

    [Fact]
    public async Task Nobody_can_delete_their_own_account() =>
        Assert.Equal(403, await StatusOf(() => _users.DeleteAsync(ManagerId, ManagerId)));

    // ── Roles ──

    [Fact]
    public async Task Only_root_can_change_a_role() =>
        Assert.Equal(403, await StatusOf(() =>
            _users.UpdateAsync(ManagerId, AgentId, new UpdateUserRequest(null, null, null, null, "manager"))));

    // ── Input policy ──

    [Fact]
    public async Task A_password_is_required_when_creating_an_account() =>
        Assert.Equal(400, await StatusOf(() =>
            _users.CreateAsync(RootId, new CreateUserRequest("A", "B", "new@x.com", null))));

    [Fact]
    public async Task A_short_password_is_refused() =>
        Assert.Equal(400, await StatusOf(() =>
            _users.CreateAsync(RootId, new CreateUserRequest("A", "B", "new@x.com", "short"))));

    [Fact]
    public async Task A_malformed_email_is_refused() =>
        Assert.Equal(400, await StatusOf(() =>
            _users.CreateAsync(RootId, new CreateUserRequest("A", "B", "not-an-email", "a-good-password"))));

    [Fact]
    public async Task A_duplicate_email_is_refused_case_insensitively() =>
        Assert.Equal(409, await StatusOf(() =>
            _users.CreateAsync(RootId, new CreateUserRequest("A", "B", "AGENT@X.COM", "a-good-password"))));

    // ── Audit trail ──

    [Fact]
    public async Task Disabling_an_account_is_recorded()
    {
        await _users.UpdateStatusAsync(RootId, AgentId, "disabled");

        var entry = Assert.Single(await _db.AuditEntries
            .Where(e => e.Action == AuditService.Actions.UserStatusChanged).ToListAsync());
        Assert.Equal("root@x.com", entry.ActorEmail);
        Assert.Equal("agent@x.com", entry.TargetLabel);
        Assert.Equal("active -> disabled", entry.Detail);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_progress_and_locks()
    {
        _db.Assignments.Add(new Assignment
        {
            CourseId = 101, AgentId = AgentId, CompletedItemIdsJson = "[]",
            Status = "not_started", AssignedAt = DateTime.UtcNow
        });
        _db.Courses.Add(new Course { Id = 101, Title = "C", Description = "", Status = "published" });
        await _db.SaveChangesAsync();

        await _users.DeleteAsync(RootId, AgentId);

        Assert.Empty(await _db.Assignments.ToListAsync());
        Assert.Empty(await _db.Users.Where(u => u.Id == AgentId).ToListAsync());
    }

    public void Dispose() => _db.Dispose();
}

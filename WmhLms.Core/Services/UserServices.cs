using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Dtos;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

public static class Mapping
{
    public static string NameOf(User u) => $"{u.FirstName} {u.LastName}".Trim();
    public static UserDto ToDto(User u) => new(u.Id, u.FirstName, u.LastName,
        NameOf(u), u.Email, u.Role, u.Status, u.IsRoot, u.CreatedAt);
    public static OptionDto ToDto(Option o) => new(o.Id, o.Text, o.IsCorrect);
    public static QuestionDto ToDto(Question q) => new(q.Id, q.ItemId, q.Type, q.Prompt,
        q.Options.Select(ToDto).ToList());
    public static ItemDto ToDto(Item i) => new(i.Id, i.SectionId, i.Title, i.Type,
        i.ContentUrl, i.TextContent, i.Questions.Select(ToDto).ToList());
    public static SectionDto ToDto(Section s) => new(s.Id, s.CourseId, s.Title, s.Order,
        s.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).Select(ToDto).ToList());
    public static CourseDto ToDto(Course c) => new(c.Id, c.Title, c.Description ?? "",
        c.Status, c.CreatedAt, c.Sections.OrderBy(s => s.Order).Select(ToDto).ToList());
}

public class AuthService(AppDbContext db, IPasswordService passwords, ITokenService tokens,
    FailedLoginTracker failedLogins)
{
    public async Task<(string Token, UserDto User)> LoginAsync(string email, string password)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        failedLogins.EnsureNotBlocked(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        // Identical response for unknown email vs wrong password (no enumeration).
        // An unknown email is counted too: skipping it would let the lockout
        // answer "does this account exist" by never arriving.
        if (user is null || !passwords.Verify(user, password ?? ""))
        {
            failedLogins.RecordFailure(email);
            throw ApiException.Unauthorized();
        }
        failedLogins.Clear(email);
        if (user.Status == "disabled")
            throw ApiException.Forbidden("This account has been disabled. Please contact your administrator.");
        return (tokens.Create(user), Mapping.ToDto(user));
    }

    public async Task<UserDto> MeAsync(long userId)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw ApiException.Unauthorized("Session expired.");
        if (user.Status == "disabled") throw ApiException.Forbidden("Account disabled.");
        return Mapping.ToDto(user);
    }
}

public class UserService(AppDbContext db, IPasswordService passwords, AuditService audit)
{
    private static readonly string[] Roles = ["manager", "agent"];
    private static readonly string[] Statuses = ["active", "disabled"];

    public async Task<PagedResult<UserDto>> ListAsync(string? role, string? status,
        string? search, int page, int limit)
    {
        (page, limit) = Guard.Paging(page, limit);
        var q = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(role)) q = q.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            q = q.Where(u => (u.FirstName + " " + u.LastName).ToLower().Contains(s)
                || u.Email.ToLower().Contains(s));
        }
        var total = await q.CountAsync();
        var items = await q.OrderBy(u => u.Id)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        return PagedResult<UserDto>.Of(items.Select(Mapping.ToDto).ToList(), page, limit, total);
    }

    private async Task<User> CallerAsync(long callerId) =>
        await db.Users.FindAsync(callerId) ?? throw ApiException.Unauthorized("Session expired.");

    /// <summary>Root-only gate for actions that inspect or govern other managers.</summary>
    public async Task<User> RequireRootAsync(long callerId)
    {
        var caller = await CallerAsync(callerId);
        if (!caller.IsRoot)
            throw ApiException.Forbidden("Only the root account can perform this action.");
        return caller;
    }

    // Managers are peers. Without this, any manager could reset another
    // manager password and take over that account. Only the root account
    // administers managers, mirroring the existing create-manager rule.
    private static void RequireRootToAdministerManagers(User caller, User target, string verb)
    {
        if (target.Role == "manager" && !caller.IsRoot)
            throw ApiException.Forbidden($"Only the root account can {verb} managers.");
    }

    public async Task<UserDto> CreateAsync(long callerId, CreateUserRequest req)
    {
        var caller = await CallerAsync(callerId);
        var firstName = Guard.RequiredText(req.FirstName, "First name", 100);
        var lastName = Guard.RequiredText(req.LastName, "Last name", 100);
        var email = Guard.Email(req.Email);
        var role = Guard.OneOf(string.IsNullOrWhiteSpace(req.Role) ? "agent" : req.Role, Roles, "role");
        if (role == "manager" && !caller.IsRoot)
            throw ApiException.Forbidden("Only the root account can create managers.");
        if (await db.Users.AnyAsync(u => u.Email == email))
            throw ApiException.Conflict("User with this email already exists.");

        var user = new User
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            // A password is mandatory: an account nobody can sign in to is not a
            // convenience, and there is no reset flow to rescue it.
            PasswordHash = passwords.Hash(Guard.Password(req.Password)),
            Role = role,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        audit.Record(caller, AuditService.Actions.UserCreated, "user", null,
            user.Email, $"role={user.Role}");
        await db.SaveChangesAsync();
        return Mapping.ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(long callerId, long userId, UpdateUserRequest req)
    {
        var caller = await CallerAsync(callerId);
        var user = await db.Users.FindAsync(userId) ?? throw ApiException.NotFound("User not found");
        if (user.IsRoot) throw ApiException.Forbidden("The root account cannot be edited.");
        RequireRootToAdministerManagers(caller, user, "edit");

        if (req.Role is not null && req.Role != user.Role)
        {
            var role = Guard.OneOf(req.Role, Roles, "role");
            if (!caller.IsRoot) throw ApiException.Forbidden("Only the root account can change roles.");
            audit.Record(caller, AuditService.Actions.UserRoleChanged, "user", user.Id,
                user.Email, $"{user.Role} -> {role}");
            user.Role = role;
        }
        if (req.FirstName is not null) user.FirstName = Guard.RequiredText(req.FirstName, "First name", 100);
        if (req.LastName is not null) user.LastName = Guard.RequiredText(req.LastName, "Last name", 100);
        if (req.Email is not null)
        {
            var email = Guard.Email(req.Email);
            if (await db.Users.AnyAsync(u => u.Id != userId && u.Email == email))
                throw ApiException.Conflict("User with this email already exists.");
            user.Email = email;
        }
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            user.PasswordHash = passwords.Hash(Guard.Password(req.Password));
            audit.Record(caller, AuditService.Actions.UserPasswordChanged, "user", user.Id,
                user.Email);
        }
        audit.Record(caller, AuditService.Actions.UserUpdated, "user", user.Id, user.Email);
        await db.SaveChangesAsync();
        return Mapping.ToDto(user);
    }

    public async Task<UserDto> UpdateStatusAsync(long callerId, long userId, string status)
    {
        var caller = await CallerAsync(callerId);
        Guard.OneOf(status, Statuses, "status");
        var user = await db.Users.FindAsync(userId) ?? throw ApiException.NotFound("User not found");
        if (user.IsRoot) throw ApiException.Forbidden("The root account cannot be disabled.");
        if (user.Id == caller.Id) throw ApiException.Forbidden("You cannot change your own status.");
        RequireRootToAdministerManagers(caller, user, "disable");
        audit.Record(caller, AuditService.Actions.UserStatusChanged, "user", user.Id,
            user.Email, $"{user.Status} -> {status}");
        user.Status = status;
        await db.SaveChangesAsync();
        return Mapping.ToDto(user);
    }

    public async Task DeleteAsync(long callerId, long userId)
    {
        var caller = await CallerAsync(callerId);
        var user = await db.Users.FindAsync(userId) ?? throw ApiException.NotFound("User not found");
        if (user.IsRoot) throw ApiException.Forbidden("The root account cannot be deleted.");
        if (user.Id == caller.Id) throw ApiException.Forbidden("You cannot delete your own account.");
        RequireRootToAdministerManagers(caller, user, "delete");
        // Explicit rather than relying on the FK cascade, so the behaviour is
        // identical on any provider and visible at the call site.
        db.QuizLocks.RemoveRange(db.QuizLocks.Where(l => l.AgentId == userId));
        db.Assignments.RemoveRange(db.Assignments.Where(a => a.AgentId == userId));
        db.Users.Remove(user);
        audit.Record(caller, AuditService.Actions.UserDeleted, "user", user.Id,
            user.Email, $"role={user.Role}");
        await db.SaveChangesAsync();
    }

    public async Task<List<object>> GetAssignmentsAsync(long userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId))
            throw ApiException.NotFound("User not found");
        var assignments = await db.Assignments.Where(a => a.AgentId == userId).ToListAsync();
        var courseIds = assignments.Select(a => a.CourseId).Distinct().ToList();
        var courses = await db.Courses
            .Where(c => courseIds.Contains(c.Id))
            .Include(c => c.Sections).ThenInclude(s => s.Items)
            .ToDictionaryAsync(c => c.Id);

        return assignments.Select(a =>
        {
            courses.TryGetValue(a.CourseId, out var c);
            var itemIds = c is null
                ? new HashSet<long>()
                : c.Sections.SelectMany(s => s.Items).Select(i => i.Id).ToHashSet();
            // Count only completions that still point at items in this course.
            var doneIds = JsonIds.Read(a.CompletedItemIdsJson).Where(itemIds.Contains).ToList();
            return (object)new Dictionary<string, object?>
            {
                ["course_id"] = a.CourseId,
                ["course_title"] = c?.Title ?? "Unknown Course",
                ["status"] = a.Status,
                ["progress"] = Progress.Percent(doneIds.Count, itemIds.Count),
                ["total_items"] = itemIds.Count,
                ["completed_item_ids"] = doneIds,
                ["assigned_at"] = a.AssignedAt,
                ["completed_at"] = a.CompletedAt
            };
        }).ToList();
    }
}

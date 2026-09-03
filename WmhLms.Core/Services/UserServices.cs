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

public class AuthService(AppDbContext db, IPasswordService passwords, ITokenService tokens)
{
    public async Task<(string Token, UserDto User)> LoginAsync(string email, string password)
    {
        email = (email ?? "").Trim();
        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email.ToLower() == email.ToLower());
        // Identical response for unknown email vs wrong password (no enumeration).
        if (user is null || !passwords.Verify(user, password ?? ""))
            throw ApiException.Unauthorized();
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

public class UserService(AppDbContext db, IPasswordService passwords)
{
    private static readonly string[] Roles = ["manager", "agent"];
    private static readonly string[] Statuses = ["active", "disabled"];

    public async Task<PagedResult<UserDto>> ListAsync(string? role, string? status,
        string? search, int page, int limit)
    {
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
        await db.Users.FindAsync(callerId) ?? throw ApiException.Unauthorized();

    public async Task<UserDto> CreateAsync(long callerId, CreateUserRequest req)
    {
        var caller = await CallerAsync(callerId);
        if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName)
            || string.IsNullOrWhiteSpace(req.Email))
            throw ApiException.BadRequest("First name, last name and email are required.");
        var role = string.IsNullOrWhiteSpace(req.Role) ? "agent" : req.Role;
        if (!Roles.Contains(role)) throw ApiException.BadRequest("Invalid role.");
        if (role == "manager" && !caller.IsRoot)
            throw ApiException.Forbidden("Only the root account can create managers.");
        var email = req.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            throw ApiException.Conflict("User with this email already exists.");
        var now = DateTime.UtcNow;
        var user = new User
        {
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            Email = email,
            PasswordHash = passwords.Hash(string.IsNullOrWhiteSpace(req.Password)
                ? Guid.NewGuid().ToString("N") : req.Password),
            Role = role,
            Status = "active",
            CreatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Mapping.ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(long callerId, long userId, UpdateUserRequest req)
    {
        var caller = await CallerAsync(callerId);
        var user = await db.Users.FindAsync(userId) ?? throw ApiException.NotFound("User not found");
        if (user.IsRoot) throw ApiException.Forbidden("The root account cannot be edited.");
        if (req.Role is not null && req.Role != user.Role)
        {
            if (!Roles.Contains(req.Role)) throw ApiException.BadRequest("Invalid role.");
            if (!caller.IsRoot) throw ApiException.Forbidden("Only the root account can change roles.");
            user.Role = req.Role;
        }
        if (req.FirstName is not null) user.FirstName = req.FirstName;
        if (req.LastName is not null) user.LastName = req.LastName;
        if (req.Email is not null)
        {
            var email = req.Email.Trim();
            if (await db.Users.AnyAsync(u => u.Id != userId && u.Email.ToLower() == email.ToLower()))
                throw ApiException.Conflict("User with this email already exists.");
            user.Email = email;
        }
        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = passwords.Hash(req.Password);
        await db.SaveChangesAsync();
        return Mapping.ToDto(user);
    }

    public async Task<UserDto> UpdateStatusAsync(long callerId, long userId, string status)
    {
        var caller = await CallerAsync(callerId);
        if (!Statuses.Contains(status)) throw ApiException.BadRequest("Invalid status.");
        var user = await db.Users.FindAsync(userId) ?? throw ApiException.NotFound("User not found");
        if (user.IsRoot) throw ApiException.Forbidden("The root account cannot be disabled.");
        user.Status = status;
        await db.SaveChangesAsync();
        return Mapping.ToDto(user);
    }

    public async Task DeleteAsync(long callerId, long userId)
    {
        var caller = await CallerAsync(callerId);
        var user = await db.Users.FindAsync(userId) ?? throw ApiException.NotFound("User not found");
        if (user.IsRoot) throw ApiException.Forbidden("The root account cannot be deleted.");
        if (user.Role == "manager" && !caller.IsRoot)
            throw ApiException.Forbidden("Only the root account can delete managers.");
        db.Assignments.RemoveRange(db.Assignments.Where(a => a.AgentId == userId));
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    public async Task<List<object>> GetAssignmentsAsync(long userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId))
            throw ApiException.NotFound("User not found");
        var courses = await db.Courses.Include(c => c.Sections)
            .ThenInclude(s => s.Items).ToDictionaryAsync(c => c.Id);
        var list = await db.Assignments.Where(a => a.AgentId == userId).ToListAsync();
        return list.Select(a =>
        {
            courses.TryGetValue(a.CourseId, out var c);
            var total = c?.Sections.Sum(s => s.Items.Count) ?? 0;
            var done = JsonIds.Read(a.CompletedItemIdsJson).Count;
            return (object)new Dictionary<string, object?>
            {
                ["course_id"] = a.CourseId,
                ["course_title"] = c?.Title ?? "Unknown Course",
                ["status"] = a.Status,
                ["progress"] = Progress.Percent(done, total),
                ["total_items"] = total,
                ["completed_item_ids"] = JsonIds.Read(a.CompletedItemIdsJson),
                ["assigned_at"] = a.AssignedAt,
                ["completed_at"] = a.CompletedAt
            };
        }).ToList();
    }
}

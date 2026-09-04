using Microsoft.EntityFrameworkCore;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

/// <summary>
/// Writes the administrative audit trail. Entries are appended in the same
/// SaveChanges as the change they describe, so an action and its record either
/// both land or neither does.
/// </summary>
public class AuditService(AppDbContext db, TimeProvider time)
{
    public static class Actions
    {
        public const string UserCreated = "user.created";
        public const string UserUpdated = "user.updated";
        public const string UserPasswordChanged = "user.password_changed";
        public const string UserRoleChanged = "user.role_changed";
        public const string UserStatusChanged = "user.status_changed";
        public const string UserDeleted = "user.deleted";
    }

    /// <summary>Queues an entry. The caller's SaveChangesAsync commits it.</summary>
    public void Record(User? actor, string action, string targetType,
        long? targetId, string targetLabel, string detail = "")
    {
        db.Set<AuditEntry>().Add(new AuditEntry
        {
            ActorId = actor?.Id,
            ActorEmail = actor?.Email ?? "system",
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            TargetLabel = targetLabel,
            Detail = detail,
            CreatedAt = time.GetUtcNow().UtcDateTime
        });
    }

    public async Task<List<AuditEntry>> RecentAsync(int limit = 100) =>
        await db.Set<AuditEntry>()
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, Guard.MaxPageSize))
            .ToListAsync();
}

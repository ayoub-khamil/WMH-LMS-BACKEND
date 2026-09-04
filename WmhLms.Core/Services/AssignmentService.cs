using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Dtos;
using WmhLms.Data;

namespace WmhLms.Core.Services;

public class AssignmentService(AppDbContext db)
{
    public async Task AssignBulkAsync(BulkAssignmentRequest req)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == req.CourseId))
            throw ApiException.NotFound("Course not found");
        var requested = (req.AgentIds ?? []).Distinct().ToList();
        if (requested.Count == 0) return;

        // Two set queries instead of two round-trips per agent.
        var known = await db.Users.Where(u => requested.Contains(u.Id))
            .Select(u => u.Id).ToListAsync();
        var already = await db.Assignments
            .Where(a => a.CourseId == req.CourseId && requested.Contains(a.AgentId))
            .Select(a => a.AgentId).ToListAsync();

        var toAdd = known.Except(already).ToList();
        if (toAdd.Count == 0) return;

        var now = DateTime.UtcNow;
        db.Assignments.AddRange(toAdd.Select(agentId => new Data.Entities.Assignment
        {
            CourseId = req.CourseId, AgentId = agentId,
            CompletedItemIdsJson = "[]", Status = "not_started", AssignedAt = now
        }));
        await db.SaveChangesAsync();
    }

    public async Task UnassignBulkAsync(BulkAssignmentRequest req)
    {
        var ids = (req.AgentIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return;
        // Locks belong to the enrolment: leaving them behind would silently
        // re-lock the learner if they were ever assigned the course again.
        db.QuizLocks.RemoveRange(db.QuizLocks.Where(l =>
            l.CourseId == req.CourseId && ids.Contains(l.AgentId)));
        db.Assignments.RemoveRange(db.Assignments.Where(a =>
            a.CourseId == req.CourseId && ids.Contains(a.AgentId)));
        await db.SaveChangesAsync();
    }

    public async Task<List<object>> GetCourseAssignmentsAsync(long courseId)
    {
        var course = await db.Courses.Include(c => c.Sections).ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw ApiException.NotFound("Course not found");
        var itemIds = course.Sections.SelectMany(s => s.Items).Select(i => i.Id).ToHashSet();
        var assignments = await db.Assignments.Where(a => a.CourseId == courseId).ToListAsync();
        var agentIds = assignments.Select(a => a.AgentId).Distinct().ToList();
        var agents = await db.Users.Where(u => agentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return assignments.Select(a =>
        {
            agents.TryGetValue(a.AgentId, out var u);
            // Count only completions that still point at items in this course.
            var done = JsonIds.Read(a.CompletedItemIdsJson).Where(itemIds.Contains).Distinct().Count();
            return (object)new Dictionary<string, object?>
            {
                ["agent_id"] = a.AgentId,
                ["agent_name"] = u is null ? $"Agent #{a.AgentId}" : Mapping.NameOf(u),
                ["agent_email"] = u?.Email ?? "",
                ["status"] = a.Status,
                ["progress"] = Progress.Percent(done, itemIds.Count),
                ["completed_items_count"] = done,
                ["total_items"] = itemIds.Count,
                ["assigned_at"] = a.AssignedAt,
                ["completed_at"] = a.CompletedAt
            };
        }).ToList();
    }
}

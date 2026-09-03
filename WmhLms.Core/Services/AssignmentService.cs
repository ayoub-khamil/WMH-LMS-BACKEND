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
        var now = DateTime.UtcNow;
        foreach (var agentId in req.AgentIds.Distinct())
        {
            if (!await db.Users.AnyAsync(u => u.Id == agentId)) continue;
            var exists = await db.Assignments.AnyAsync(a =>
                a.CourseId == req.CourseId && a.AgentId == agentId);
            if (exists) continue;
            db.Assignments.Add(new Data.Entities.Assignment
            {
                CourseId = req.CourseId, AgentId = agentId,
                CompletedItemIdsJson = "[]", Status = "not_started", AssignedAt = now
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task UnassignBulkAsync(BulkAssignmentRequest req)
    {
        var ids = req.AgentIds.Distinct().ToList();
        db.Assignments.RemoveRange(db.Assignments.Where(a =>
            a.CourseId == req.CourseId && ids.Contains(a.AgentId)));
        await db.SaveChangesAsync();
    }

    public async Task<List<object>> GetCourseAssignmentsAsync(long courseId)
    {
        var course = await db.Courses.Include(c => c.Sections).ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw ApiException.NotFound("Course not found");
        var total = course.Sections.Sum(s => s.Items.Count);
        var list = await db.Assignments.Where(a => a.CourseId == courseId).ToListAsync();
        var agentIds = list.Select(a => a.AgentId).Distinct().ToList();
        var agents = await db.Users.Where(u => agentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
        return list.Select(a =>
        {
            agents.TryGetValue(a.AgentId, out var u);
            var done = JsonIds.Read(a.CompletedItemIdsJson).Count;
            return (object)new Dictionary<string, object?>
            {
                ["agent_id"] = a.AgentId,
                ["agent_name"] = u is null ? $"Agent #{a.AgentId}" : Mapping.NameOf(u),
                ["agent_email"] = u?.Email ?? "",
                ["status"] = a.Status,
                ["progress"] = Progress.Percent(done, total),
                ["completed_items_count"] = done,
                ["total_items"] = total,
                ["assigned_at"] = a.AssignedAt,
                ["completed_at"] = a.CompletedAt
            };
        }).ToList();
    }
}

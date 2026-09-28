using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Dtos;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

/// <summary>
/// Agents' private notes, one notepad per course item.
///
/// Callers pass the id of the signed-in user and nothing else: unlike the
/// other learn endpoints there is no "act on behalf of an agent" path, so a
/// manager cannot read anyone's notes, even through the API. Writes follow
/// the same rules as progress: the item must belong to the course, and the
/// agent must already be enrolled in it.
/// </summary>
public class NoteService(AppDbContext db, TimeProvider time)
{
    public const int MaxLength = 10_000;

    private async Task RequireEnrolmentAsync(long agentId, long courseId)
    {
        if (!await db.Assignments.AnyAsync(a => a.CourseId == courseId && a.AgentId == agentId))
            throw ApiException.Forbidden("You are not enrolled in this course.");
    }

    public async Task<List<NoteDto>> GetCourseNotesAsync(long agentId, long courseId)
    {
        await RequireEnrolmentAsync(agentId, courseId);
        return await db.Notes.AsNoTracking()
            .Where(n => n.AgentId == agentId && n.CourseId == courseId)
            .OrderBy(n => n.ItemId)
            .Select(n => new NoteDto(n.ItemId, n.Body, n.UpdatedAt))
            .ToListAsync();
    }

    /// <summary>Creates or replaces the note. An empty body deletes it and returns null.</summary>
    public async Task<NoteDto?> SaveAsync(long agentId, long itemId, SaveNoteRequest req)
    {
        var body = req.Body ?? "";
        if (body.Length > MaxLength)
            throw ApiException.BadRequest($"Notes must be {MaxLength:N0} characters or fewer.");

        var belongsToCourse = await db.Items
            .Join(db.Sections, i => i.SectionId, s => s.Id, (i, s) => new { i.Id, s.CourseId })
            .AnyAsync(x => x.Id == itemId && x.CourseId == req.CourseId);
        if (!belongsToCourse)
            throw ApiException.BadRequest("That item does not belong to this course.");
        await RequireEnrolmentAsync(agentId, req.CourseId);

        var note = await db.Notes.FirstOrDefaultAsync(n => n.AgentId == agentId && n.ItemId == itemId);
        if (string.IsNullOrWhiteSpace(body))
        {
            if (note is not null)
            {
                db.Notes.Remove(note);
                await db.SaveChangesAsync();
            }
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (note is null)
        {
            note = new Note
            {
                AgentId = agentId, CourseId = req.CourseId, ItemId = itemId,
                CreatedAt = now
            };
            db.Notes.Add(note);
        }
        note.Body = body;
        note.UpdatedAt = now;
        await db.SaveChangesAsync();
        return new NoteDto(note.ItemId, note.Body, note.UpdatedAt);
    }
}

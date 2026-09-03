using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmhLms.Data;

namespace WmhLms.Api.Controllers;

/// <summary>
/// Demo helpers. 404 unless running in Development.
/// POST /api/dev/reset wipes courses, assignments, locks and every
/// non-root user, leaving only the seeded root account.
/// </summary>
[Route("api/dev")]
public class DevController(AppDbContext db, IWebHostEnvironment env) : BaseController
{
    [HttpPost("reset"), AllowAnonymous]
    public async Task<IActionResult> Reset()
    {
        if (!env.IsDevelopment()) return NotFound();
        db.QuizLocks.RemoveRange(db.QuizLocks);
        db.Assignments.RemoveRange(db.Assignments);
        db.Courses.RemoveRange(db.Courses);
        db.Users.RemoveRange(db.Users.Where(u => !u.IsRoot));
        await db.SaveChangesAsync();
        var root = await db.Users.Where(u => u.IsRoot).Select(u => u.Email).ToListAsync();
        var courses = await db.Courses.CountAsync();
        var users = await db.Users.CountAsync();
        return Ok(new { success = true, root, users, courses });
    }

    [HttpDelete("quiz-lock")]
    public async Task<IActionResult> ClearQuizLock([FromQuery] long agent_id, [FromQuery] long item_id)
    {
        if (!env.IsDevelopment()) return NotFound();
        var quizLock = await db.QuizLocks.FirstOrDefaultAsync(x =>
            x.AgentId == agent_id && x.QuizItemId == item_id);
        if (quizLock is not null)
        {
            db.QuizLocks.Remove(quizLock);
            await db.SaveChangesAsync();
        }
        return Ok(new { success = true });
    }
}

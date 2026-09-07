using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmhLms.Data;

namespace WmhLms.Api.Controllers;

/// <summary>
/// Demo helpers. Every action 404s unless running in Development, so these
/// routes do not exist in a deployed build.
///
/// POST /api/dev/reset wipes courses, assignments, locks and every non-root
/// user, leaving only the bootstrapped root account. Restart the API afterwards to
/// re-seed the demo catalogue (DemoSeed seeds users and content separately).
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
}

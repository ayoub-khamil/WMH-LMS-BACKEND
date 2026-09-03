using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WmhLms.Core.Dtos;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

/// <summary>
/// Server is the source of truth for progress, grading and quiz locks.
/// QuizLock rows persist across page reloads. Live countdowns are derived
/// from server UTC; TimeProvider is injected (not DateTime.UtcNow directly)
/// so tests can substitute fake time. Note: on a single demo machine a
/// Windows wall-clock jump also moves server UTC — the monotonic guard for
/// that case is the client polling lock status from here rather than its
/// own clock, plus short cooldowns configurable via Quiz:CooldownMinutes.
/// </summary>
public class LearnService(AppDbContext db, TimeProvider time, IConfiguration config)
{
    private int CooldownMinutes =>
        int.TryParse(config["Quiz:CooldownMinutes"], out var m) ? m : 5;

    private async Task<Course> CourseTreeAsync(long courseId) =>
        await db.Courses.Include(c => c.Sections.OrderBy(s => s.Order))
            .ThenInclude(s => s.Items.OrderBy(i => i.Order).ThenBy(i => i.Id))
            .ThenInclude(i => i.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw ApiException.NotFound("Course not found");

    private static List<Item> Flat(Course c) =>
        c.Sections.OrderBy(s => s.Order)
            .SelectMany(s => s.Items.OrderBy(i => i.Order).ThenBy(i => i.Id)).ToList();

    private async Task<Assignment?> FindAssignmentAsync(long courseId, long agentId) =>
        await db.Assignments.FirstOrDefaultAsync(a =>
            a.CourseId == courseId && a.AgentId == agentId);

    private CourseWithProgressDto WithProgress(Course c, Assignment? a)
    {
        var all = Flat(c);
        var doneIds = a is null ? new List<long>() : JsonIds.Read(a.CompletedItemIdsJson);
        var done = all.Count(i => doneIds.Contains(i.Id));
        string status = a?.Status ?? "not_started";
        if (done >= all.Count && all.Count > 0) status = "completed";
        else if (done > 0) status = "in_progress";
        return new(c.Id, c.Title, c.Description ?? "", c.Status, c.CreatedAt,
            c.Sections.OrderBy(s => s.Order).Select(Mapping.ToDto).ToList(),
            Progress.Percent(done, all.Count), doneIds, all.Count,
            a?.AssignedAt, a?.CompletedAt, status);
    }

    public async Task<LearnBucketsDto> GetCoursesAsync(long agentId)
    {
        var assigns = await db.Assignments.Where(a => a.AgentId == agentId).ToListAsync();
        var inP = new List<CourseWithProgressDto>();
        var notS = new List<CourseWithProgressDto>();
        var done = new List<CourseWithProgressDto>();
        foreach (var a in assigns)
        {
            var c = await CourseTreeAsync(a.CourseId);
            if (c.Status != "published") continue;
            var dto = WithProgress(c, a);
            if (dto.AssignmentStatus == "completed" || dto.Progress == 100) done.Add(dto);
            else if (dto.AssignmentStatus == "in_progress" || dto.CompletedItemIds.Count > 0) inP.Add(dto);
            else notS.Add(dto);
        }
        return new(inP, notS, done);
    }

    public async Task<CourseTreeDto> GetCourseTreeAsync(long courseId, long agentId)
    {
        var c = await CourseTreeAsync(courseId);
        var a = await FindAssignmentAsync(courseId, agentId);
        var doneIds = a is null ? new List<long>() : JsonIds.Read(a.CompletedItemIdsJson);
        return new(c.Id, c.Title, c.Description ?? "", c.Status, c.CreatedAt,
            c.Sections.OrderBy(s => s.Order).Select(Mapping.ToDto).ToList(),
            doneIds, a?.Status ?? "not_started");
    }

    public async Task<object> ResumeAsync(long courseId, long agentId)
    {
        var c = await CourseTreeAsync(courseId);
        var all = Flat(c);
        if (all.Count == 0) return new Dictionary<string, object?> { ["next_item_id"] = null };
        var a = await FindAssignmentAsync(courseId, agentId);
        var done = new HashSet<long>(a is null ? [] : JsonIds.Read(a.CompletedItemIdsJson));
        var next = all.FirstOrDefault(i => !done.Contains(i.Id)) ?? all[0];
        return new Dictionary<string, object?> { ["next_item_id"] = next.Id };
    }

    public async Task<object> CompleteItemAsync(CompleteItemRequest req)
    {
        var c = await CourseTreeAsync(req.CourseId);
        var a = await FindAssignmentAsync(req.CourseId, req.AgentId);
        var now = time.GetUtcNow().UtcDateTime;
        if (a is null)
        {
            a = new Assignment
            {
                CourseId = req.CourseId, AgentId = req.AgentId,
                CompletedItemIdsJson = "[]", Status = "in_progress", AssignedAt = now
            };
            db.Assignments.Add(a);
        }
        var done = JsonIds.Read(a.CompletedItemIdsJson);
        if (!done.Contains(req.ItemId)) done.Add(req.ItemId);
        a.CompletedItemIdsJson = JsonIds.Write(done);
        var total = Flat(c).Count;
        var complete = total > 0 && done.Count >= total;
        a.Status = complete ? "completed" : "in_progress";
        a.CompletedAt = complete ? now : null;
        await db.SaveChangesAsync();
        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["is_course_completed"] = complete,
            ["completed_item_ids"] = done
        };
    }

    public async Task<QuizResultDto> SubmitQuizAsync(SubmitQuizRequest req)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var quiz = await db.Items.Include(i => i.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(i => i.Id == req.ItemId && i.Type == "quiz")
            ?? throw ApiException.NotFound("Quiz item not found");

        if (quiz.Questions.Any(q => (req.Answers
                .FirstOrDefault(a => a.QuestionId == q.Id)?.SelectedOptionIds ?? []).Count == 0))
            throw ApiException.BadRequest("Please answer all questions before submitting.");

        var quizLock = await db.QuizLocks.FirstOrDefaultAsync(l =>
            l.AgentId == req.AgentId && l.QuizItemId == req.ItemId);
        if (quizLock is not null && quizLock.LockedUntilUtc > now)
        {
            var remaining = (int)Math.Ceiling((quizLock.LockedUntilUtc - now).TotalSeconds);
            throw ApiException.Locked("Quiz retake is locked. Review course content and retry after the cooldown.", remaining);
        }

        var questions = quiz.Questions;
        var incorrect = new List<long>();
        foreach (var q in questions)
        {
            var ans = req.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
            var selected = (ans?.SelectedOptionIds ?? []).ToHashSet();
            var correct = q.Options.Where(o => o.IsCorrect).Select(o => (long)o.Id).ToHashSet();
            if (!selected.SetEquals(correct)) incorrect.Add(q.Id);
        }
        var correctCount = questions.Count - incorrect.Count;
        var passed = questions.Count > 0 && incorrect.Count == 0;
        var score = $"{correctCount}/{questions.Count}";
        var pct = questions.Count == 0 ? 0 : (int)Math.Round(correctCount / (double)questions.Count * 100);

        if (passed)
        {
            if (quizLock is not null) db.QuizLocks.Remove(quizLock);
            await CompleteItemInternalAsync(req.ItemId, req.CourseId, req.AgentId, now);
            await db.SaveChangesAsync();
            return new QuizResultDto(true, score, 100, null, null);
        }

        var lockedUntil = now.AddMinutes(CooldownMinutes);
        var resultJson = JsonSerializer.Serialize(new
        {
            passed = false,
            score,
            score_percentage = pct,
            incorrect_question_ids = incorrect
        });
        if (quizLock is null)
        {
            db.QuizLocks.Add(new QuizLock
            {
                AgentId = req.AgentId, QuizItemId = req.ItemId, CourseId = req.CourseId,
                LockedUntilUtc = lockedUntil, ReviewedItemIdsJson = "[]", LastResultJson = resultJson
            });
        }
        else
        {
            quizLock.LockedUntilUtc = lockedUntil;
            quizLock.LastResultJson = resultJson;
            quizLock.ReviewedItemIdsJson = "[]";
        }
        await db.SaveChangesAsync();
        var secs = (int)Math.Ceiling((lockedUntil - now).TotalSeconds);
        return new QuizResultDto(false, score, pct, incorrect, secs);
    }

    // Server-side review gate: viewing a non-quiz item counts toward unlock.
    public async Task RecordItemViewAsync(long agentId, long courseId, long viewedItemId)
    {
        var locks = await db.QuizLocks
            .Where(l => l.AgentId == agentId && l.CourseId == courseId && l.QuizItemId != viewedItemId)
            .ToListAsync();
        foreach (var l in locks)
        {
            var ids = JsonIds.Read(l.ReviewedItemIdsJson);
            if (!ids.Contains(viewedItemId))
            {
                ids.Add(viewedItemId);
                l.ReviewedItemIdsJson = JsonIds.Write(ids);
            }
        }
        if (locks.Count > 0) await db.SaveChangesAsync();
    }

    public async Task<object?> GetLockStatusAsync(long agentId, long quizItemId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var l = await db.QuizLocks.FirstOrDefaultAsync(x =>
            x.AgentId == agentId && x.QuizItemId == quizItemId);
        if (l is null) return null;
        var remaining = Math.Max(0, (int)Math.Ceiling((l.LockedUntilUtc - now).TotalSeconds));
        var reviewed = JsonIds.Read(l.ReviewedItemIdsJson).Count > 0;
        return new Dictionary<string, object?>
        {
            ["locked_until"] = l.LockedUntilUtc,
            ["remaining_seconds"] = remaining,
            ["review_satisfied"] = reviewed,
            ["can_retry"] = remaining == 0 && reviewed,
            ["last_result"] = l.LastResultJson
        };
    }

    private async Task CompleteItemInternalAsync(long itemId, long courseId, long agentId, DateTime now)
    {
        var total = Flat(await CourseTreeAsync(courseId)).Count;
        var a = await FindAssignmentAsync(courseId, agentId);
        if (a is null)
        {
            a = new Assignment
            {
                CourseId = courseId, AgentId = agentId,
                CompletedItemIdsJson = "[]", Status = "in_progress", AssignedAt = now
            };
            db.Assignments.Add(a);
        }
        var done = JsonIds.Read(a.CompletedItemIdsJson);
        if (!done.Contains(itemId)) done.Add(itemId);
        a.CompletedItemIdsJson = JsonIds.Write(done);
        var complete = total > 0 && done.Count >= total;
        a.Status = complete ? "completed" : "in_progress";
        a.CompletedAt = complete ? now : null;
    }
}

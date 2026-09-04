using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WmhLms.Core.Dtos;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

/// <summary>
/// Server is the source of truth for progress, grading and quiz locks.
///
/// Two rules hold everywhere in this service, and every public method
/// enforces them before it writes:
///   1. An item only counts toward a course if it actually belongs to
///      that course. Client-supplied ids are never trusted.
///   2. Progress is only recorded against an assignment that already
///      exists. Learners cannot enrol themselves by calling the API.
///
/// QuizLock rows persist across page reloads. Live countdowns are derived
/// from server UTC; TimeProvider is injected (not DateTime.UtcNow directly)
/// so tests can substitute fake time. Note: on a single demo machine a
/// Windows wall-clock jump also moves server UTC - the monotonic guard for
/// that case is the client polling lock status from here rather than its
/// own clock, plus short cooldowns configurable via Quiz:CooldownMinutes.
/// </summary>
public class LearnService(AppDbContext db, TimeProvider time, IConfiguration config)
{
    private int CooldownMinutes =>
        int.TryParse(config["Quiz:CooldownMinutes"], out var m) && m >= 0 ? m : 5;

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

    private async Task<Assignment> RequireAssignmentAsync(long courseId, long agentId) =>
        await FindAssignmentAsync(courseId, agentId)
        ?? throw ApiException.Forbidden("You are not enrolled in this course.");

    /// <summary>
    /// Reconciles a stored completion list against the course as it exists now:
    /// drops ids for items that were deleted or never belonged to the course,
    /// and de-duplicates. This is what makes "done >= total" trustworthy.
    /// </summary>
    private static (List<long> Done, int Total) Reconcile(Course course, Assignment? assignment)
    {
        var itemIds = Flat(course).Select(i => i.Id).ToList();
        var valid = itemIds.ToHashSet();
        var done = assignment is null
            ? new List<long>()
            : JsonIds.Read(assignment.CompletedItemIdsJson)
                .Where(valid.Contains).Distinct().ToList();
        return (done, itemIds.Count);
    }

    private static CourseWithProgressDto WithProgress(Course c, Assignment? a)
    {
        var (done, total) = Reconcile(c, a);
        var status = a?.Status ?? "not_started";
        if (total > 0 && done.Count >= total) status = "completed";
        else if (done.Count > 0) status = "in_progress";
        return new(c.Id, c.Title, c.Description ?? "", c.Status, c.CreatedAt,
            c.Sections.OrderBy(s => s.Order).Select(Mapping.ToDto).ToList(),
            Progress.Percent(done.Count, total), done, total,
            a?.AssignedAt, a?.CompletedAt, status);
    }

    public async Task<LearnBucketsDto> GetCoursesAsync(long agentId)
    {
        var assignments = await db.Assignments.Where(a => a.AgentId == agentId).ToListAsync();
        var inProgress = new List<CourseWithProgressDto>();
        var notStarted = new List<CourseWithProgressDto>();
        var completed = new List<CourseWithProgressDto>();
        if (assignments.Count == 0) return new(inProgress, notStarted, completed);

        // One query for every assigned course rather than one per assignment.
        var courseIds = assignments.Select(a => a.CourseId).Distinct().ToList();
        var courses = await db.Courses
            .Where(c => courseIds.Contains(c.Id) && c.Status == "published")
            .Include(c => c.Sections.OrderBy(s => s.Order))
                .ThenInclude(s => s.Items.OrderBy(i => i.Order).ThenBy(i => i.Id))
                .ThenInclude(i => i.Questions).ThenInclude(q => q.Options)
            .ToDictionaryAsync(c => c.Id);

        foreach (var a in assignments)
        {
            if (!courses.TryGetValue(a.CourseId, out var course)) continue;
            var dto = WithProgress(course, a);
            if (dto.AssignmentStatus == "completed" || dto.Progress == 100) completed.Add(dto);
            else if (dto.AssignmentStatus == "in_progress" || dto.CompletedItemIds.Count > 0) inProgress.Add(dto);
            else notStarted.Add(dto);
        }
        return new(inProgress, notStarted, completed);
    }

    /// <summary>
    /// Managers may read any course tree (content preview). Agents may only
    /// read a course they are assigned to.
    /// </summary>
    public async Task<CourseTreeDto> GetCourseTreeAsync(long courseId, long agentId, bool callerIsManager)
    {
        var course = await CourseTreeAsync(courseId);
        var assignment = callerIsManager
            ? await FindAssignmentAsync(courseId, agentId)
            : await RequireAssignmentAsync(courseId, agentId);
        var (done, _) = Reconcile(course, assignment);
        return new(course.Id, course.Title, course.Description ?? "", course.Status, course.CreatedAt,
            course.Sections.OrderBy(s => s.Order).Select(Mapping.ToDto).ToList(),
            done, assignment?.Status ?? "not_started");
    }

    public async Task<object> ResumeAsync(long courseId, long agentId, bool callerIsManager)
    {
        var course = await CourseTreeAsync(courseId);
        var all = Flat(course);
        if (all.Count == 0) return new Dictionary<string, object?> { ["next_item_id"] = null };
        var assignment = callerIsManager
            ? await FindAssignmentAsync(courseId, agentId)
            : await RequireAssignmentAsync(courseId, agentId);
        var (doneList, _) = Reconcile(course, assignment);
        var done = doneList.ToHashSet();
        var next = all.FirstOrDefault(i => !done.Contains(i.Id)) ?? all[0];
        return new Dictionary<string, object?> { ["next_item_id"] = next.Id };
    }

    public async Task<object> CompleteItemAsync(CompleteItemRequest req)
    {
        var course = await CourseTreeAsync(req.CourseId);
        if (Flat(course).All(i => i.Id != req.ItemId))
            throw ApiException.BadRequest("That item does not belong to this course.");
        var assignment = await RequireAssignmentAsync(req.CourseId, req.AgentId);

        var complete = ApplyCompletion(course, assignment, req.ItemId, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["is_course_completed"] = complete,
            ["completed_item_ids"] = JsonIds.Read(assignment.CompletedItemIdsJson)
        };
    }

    public async Task<QuizResultDto> SubmitQuizAsync(SubmitQuizRequest req)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var course = await CourseTreeAsync(req.CourseId);
        // Resolving the quiz through the course tree is what proves the item
        // belongs to the course the caller claims to be working through.
        var quiz = Flat(course).FirstOrDefault(i => i.Id == req.ItemId && i.Type == "quiz")
            ?? throw ApiException.NotFound("Quiz item not found");
        var assignment = await RequireAssignmentAsync(req.CourseId, req.AgentId);

        // Lock is checked before answer validation so a locked learner always
        // gets 423 with a countdown, never a confusing 400.
        var quizLock = await db.QuizLocks.FirstOrDefaultAsync(l =>
            l.AgentId == req.AgentId && l.QuizItemId == req.ItemId);
        if (quizLock is not null)
        {
            var remaining = (int)Math.Ceiling((quizLock.LockedUntilUtc - now).TotalSeconds);
            if (remaining > 0)
                throw ApiException.Locked(
                    "Quiz retake is locked. Review course content and retry after the cooldown.", remaining);
            // The review gate was previously advisory (client-side only).
            if (JsonIds.Read(quizLock.ReviewedItemIdsJson).Count == 0)
                throw ApiException.Locked(
                    "Review at least one other item in this course before retrying the assessment.", 0);
        }

        var questions = quiz.Questions;
        if (questions.Any(q => (req.Answers?
                .FirstOrDefault(a => a.QuestionId == q.Id)?.SelectedOptionIds ?? []).Count == 0))
            throw ApiException.BadRequest("Please answer all questions before submitting.");

        var incorrect = new List<long>();
        foreach (var q in questions)
        {
            var answer = req.Answers?.FirstOrDefault(a => a.QuestionId == q.Id);
            var selected = (answer?.SelectedOptionIds ?? []).ToHashSet();
            var correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
            if (!selected.SetEquals(correct)) incorrect.Add(q.Id);
        }
        var correctCount = questions.Count - incorrect.Count;
        var passed = questions.Count > 0 && incorrect.Count == 0;
        var score = $"{correctCount}/{questions.Count}";
        var pct = questions.Count == 0 ? 0 : (int)Math.Round(correctCount / (double)questions.Count * 100);

        if (passed)
        {
            if (quizLock is not null) db.QuizLocks.Remove(quizLock);
            ApplyCompletion(course, assignment, req.ItemId, now);
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
        var seconds = (int)Math.Ceiling((lockedUntil - now).TotalSeconds);
        return new QuizResultDto(false, score, pct, incorrect, seconds);
    }

    /// <summary>Server-side review gate: viewing another item in the course counts toward unlock.</summary>
    public async Task RecordItemViewAsync(long agentId, long courseId, long viewedItemId)
    {
        var belongsToCourse = await db.Items
            .Join(db.Sections, i => i.SectionId, s => s.Id, (i, s) => new { i.Id, s.CourseId })
            .AnyAsync(x => x.Id == viewedItemId && x.CourseId == courseId);
        if (!belongsToCourse)
            throw ApiException.BadRequest("That item does not belong to this course.");

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
            // Kept as a JSON string: the client parses it with JSON.parse.
            ["last_result"] = l.LastResultJson
        };
    }

    /// <summary>
    /// Marks one item complete on an existing assignment and recomputes course
    /// status from the reconciled list. Does not save - callers batch the write.
    /// </summary>
    private static bool ApplyCompletion(Course course, Assignment assignment, long itemId, DateTime now)
    {
        var (done, total) = Reconcile(course, assignment);
        if (!done.Contains(itemId)) done.Add(itemId);
        assignment.CompletedItemIdsJson = JsonIds.Write(done);
        var complete = total > 0 && done.Count >= total;
        assignment.Status = complete ? "completed" : "in_progress";
        assignment.CompletedAt = complete ? now : null;
        return complete;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Tests;

/// <summary>
/// Grading, progress reconciliation and the quiz lock. These are the rules a
/// learner's record depends on, so they are covered against a real SQLite
/// database rather than a mock.
/// </summary>
public class LearnServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly LearnService _learn;

    public LearnServiceTests()
    {
        _db = TestDb.Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Quiz:CooldownMinutes"] = "5" })
            .Build();
        _learn = new LearnService(_db, _clock, config);
        Seed();
    }

    private void Seed()
    {
        _db.Users.Add(new User { Id = 2, FirstName = "A", LastName = "B", Email = "a@b.com", Role = "agent", PasswordHash = "x" });
        _db.Courses.Add(new Course
        {
            Id = 101, Title = "Course", Description = "", Status = "published",
            Sections =
            [
                new Section
                {
                    Id = 1, CourseId = 101, Title = "S1", Order = 1,
                    Items =
                    [
                        new Item { Id = 11, SectionId = 1, Title = "Text", Type = "text", Order = 1 },
                        new Item
                        {
                            Id = 12, SectionId = 1, Title = "Quiz", Type = "quiz", Order = 2,
                            Questions =
                            [
                                new Question
                                {
                                    Id = 201, ItemId = 12, Type = "multiple_choice", Prompt = "Q",
                                    Options =
                                    [
                                        new Option { Id = 1, QuestionId = 201, Text = "right", IsCorrect = true },
                                        new Option { Id = 2, QuestionId = 201, Text = "wrong", IsCorrect = false }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        });
        // A second course, so cross-course leakage can be tested.
        _db.Courses.Add(new Course
        {
            Id = 102, Title = "Other", Description = "", Status = "published",
            Sections = [new Section { Id = 2, CourseId = 102, Title = "S", Order = 1,
                Items = [new Item { Id = 21, SectionId = 2, Title = "Other item", Type = "text", Order = 1 }] }]
        });
        _db.SaveChanges();
    }

    private void Enrol(long courseId = 101) =>
        _db.Assignments.Add(new Assignment
        {
            CourseId = courseId, AgentId = 2, CompletedItemIdsJson = "[]",
            Status = "not_started", AssignedAt = _clock.GetUtcNow().UtcDateTime
        });

    // ── Enrolment ──

    [Fact]
    public async Task Completing_an_item_without_an_assignment_is_refused()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learn.CompleteItemAsync(new CompleteItemRequest(11, 101, 2)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Reading_a_course_without_an_assignment_is_refused()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learn.GetCourseTreeAsync(101, 2, callerIsManager: false));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Managers_may_read_a_course_they_are_not_enrolled_in()
    {
        var tree = await _learn.GetCourseTreeAsync(101, 2, callerIsManager: true);
        Assert.Equal(101, tree.Id);
    }

    // ── Item / course membership ──

    [Fact]
    public async Task An_item_from_another_course_cannot_be_completed()
    {
        Enrol();
        await _db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learn.CompleteItemAsync(new CompleteItemRequest(21, 101, 2)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Progress_ignores_stored_ids_that_are_not_in_the_course()
    {
        // A record carrying junk ids must not inflate completion.
        _db.Assignments.Add(new Assignment
        {
            CourseId = 101, AgentId = 2, CompletedItemIdsJson = "[11,999,21,11]",
            Status = "in_progress", AssignedAt = _clock.GetUtcNow().UtcDateTime
        });
        await _db.SaveChangesAsync();

        var buckets = await _learn.GetCoursesAsync(2);
        var course = Assert.Single(buckets.InProgress);
        Assert.Equal([11L], course.CompletedItemIds);
        Assert.Equal(2, course.TotalItems);
        Assert.Equal(50, course.Progress);
    }

    [Fact]
    public async Task Course_completes_only_when_every_item_is_done()
    {
        Enrol();
        await _db.SaveChangesAsync();

        var first = (Dictionary<string, object?>)await _learn.CompleteItemAsync(new(11, 101, 2));
        Assert.Equal(false, first["is_course_completed"]);

        var second = (Dictionary<string, object?>)await _learn.CompleteItemAsync(new(12, 101, 2));
        Assert.Equal(true, second["is_course_completed"]);

        var assignment = await _db.Assignments.SingleAsync();
        Assert.Equal("completed", assignment.Status);
        Assert.NotNull(assignment.CompletedAt);
    }

    // ── Grading ──

    [Fact]
    public async Task All_correct_passes_and_completes_the_item()
    {
        Enrol();
        await _db.SaveChangesAsync();

        var result = await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2,
            [new QuizAnswerRequest(201, [1])]));

        Assert.True(result.Passed);
        Assert.Equal("1/1", result.Score);
        Assert.Equal(100, result.ScorePercentage);
        Assert.Empty(await _db.QuizLocks.ToListAsync());
    }

    [Fact]
    public async Task A_wrong_answer_fails_and_starts_a_cooldown()
    {
        Enrol();
        await _db.SaveChangesAsync();

        var result = await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2,
            [new QuizAnswerRequest(201, [2])]));

        Assert.False(result.Passed);
        Assert.Equal([201L], result.IncorrectQuestionIds);
        Assert.Equal(300, result.LockedSecondsRemaining);
        Assert.Single(await _db.QuizLocks.ToListAsync());
    }

    [Fact]
    public async Task Partially_selecting_a_multi_answer_question_is_wrong()
    {
        _db.Questions.Add(new Question
        {
            Id = 202, ItemId = 12, Type = "multiple_answer", Prompt = "Pick both",
            Options =
            [
                new Option { Id = 3, QuestionId = 202, Text = "a", IsCorrect = true },
                new Option { Id = 4, QuestionId = 202, Text = "b", IsCorrect = true }
            ]
        });
        Enrol();
        await _db.SaveChangesAsync();

        var result = await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2,
            [new QuizAnswerRequest(201, [1]), new QuizAnswerRequest(202, [3])]));

        Assert.False(result.Passed);
        Assert.Equal([202L], result.IncorrectQuestionIds);
    }

    // ── Cooldown and review gate ──

    [Fact]
    public async Task Retrying_inside_the_cooldown_is_locked()
    {
        Enrol();
        await _db.SaveChangesAsync();
        await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [new QuizAnswerRequest(201, [2])]));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [new QuizAnswerRequest(201, [1])])));

        Assert.Equal(423, ex.StatusCode);
    }

    [Fact]
    public async Task After_the_cooldown_the_review_gate_still_applies()
    {
        Enrol();
        await _db.SaveChangesAsync();
        await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [new QuizAnswerRequest(201, [2])]));

        _clock.Advance(TimeSpan.FromMinutes(6));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [new QuizAnswerRequest(201, [1])])));
        Assert.Equal(423, ex.StatusCode);
        Assert.Contains("Review", ex.Message);
    }

    [Fact]
    public async Task Viewing_another_item_then_waiting_unlocks_the_retry()
    {
        Enrol();
        await _db.SaveChangesAsync();
        await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [new QuizAnswerRequest(201, [2])]));

        await _learn.RecordItemViewAsync(2, 101, 11);
        _clock.Advance(TimeSpan.FromMinutes(6));

        var result = await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2,
            [new QuizAnswerRequest(201, [1])]));
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task A_locked_learner_gets_423_even_with_blank_answers()
    {
        // The lock is checked before answer validation, so the learner sees a
        // countdown rather than a confusing "answer all questions" error.
        Enrol();
        await _db.SaveChangesAsync();
        await _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [new QuizAnswerRequest(201, [2])]));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _learn.SubmitQuizAsync(new SubmitQuizRequest(12, 101, 2, [])));
        Assert.Equal(423, ex.StatusCode);
    }

    [Fact]
    public async Task Unpublished_courses_are_left_out_of_the_dashboard()
    {
        var course = await _db.Courses.SingleAsync(c => c.Id == 101);
        course.Status = "draft";
        Enrol();
        await _db.SaveChangesAsync();

        var buckets = await _learn.GetCoursesAsync(2);
        Assert.Empty(buckets.InProgress);
        Assert.Empty(buckets.NotStarted);
        Assert.Empty(buckets.Completed);
    }

    public void Dispose() => _db.Dispose();
}

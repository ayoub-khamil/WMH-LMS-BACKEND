using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Tests;

/// <summary>
/// Agents' private notes: who may write them, whose they are, and that they
/// disappear with the enrolment, item, course or account they belong to.
/// </summary>
public class NoteServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly NoteService _notes;

    private const long AgentId = 2;
    private const long OtherAgentId = 3;

    public NoteServiceTests()
    {
        _db = TestDb.Create();
        _notes = new NoteService(_db, _clock);

        _db.Users.AddRange(
            new User { Id = AgentId, FirstName = "A", LastName = "B", Email = "a@b.com", Role = "agent", PasswordHash = "x" },
            new User { Id = OtherAgentId, FirstName = "C", LastName = "D", Email = "c@d.com", Role = "agent", PasswordHash = "x" });
        _db.Courses.Add(new Course
        {
            Id = 101, Title = "Course", Description = "", Status = "published",
            Sections = [new Section { Id = 1, CourseId = 101, Title = "S1", Order = 1,
                Items =
                [
                    new Item { Id = 11, SectionId = 1, Title = "Video", Type = "video", Order = 1 },
                    new Item { Id = 12, SectionId = 1, Title = "Quiz", Type = "quiz", Order = 2 }
                ] }]
        });
        _db.Courses.Add(new Course
        {
            Id = 102, Title = "Other", Description = "", Status = "published",
            Sections = [new Section { Id = 2, CourseId = 102, Title = "S", Order = 1,
                Items = [new Item { Id = 21, SectionId = 2, Title = "Other item", Type = "text", Order = 1 }] }]
        });
        _db.SaveChanges();
    }

    private void Enrol(long agentId, long courseId = 101) =>
        _db.Assignments.Add(new Assignment
        {
            CourseId = courseId, AgentId = agentId, CompletedItemIdsJson = "[]",
            Status = "not_started", AssignedAt = _clock.GetUtcNow().UtcDateTime
        });

    private static async Task<int> StatusOf(Func<Task> act) =>
        (await Assert.ThrowsAsync<ApiException>(act)).StatusCode;

    // ── Access ──

    [Fact]
    public async Task Writing_a_note_without_an_enrolment_is_refused() =>
        Assert.Equal(403, await StatusOf(() =>
            _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "note"))));

    [Fact]
    public async Task Reading_notes_without_an_enrolment_is_refused() =>
        Assert.Equal(403, await StatusOf(() => _notes.GetCourseNotesAsync(AgentId, 101)));

    [Fact]
    public async Task A_note_cannot_target_an_item_from_another_course()
    {
        Enrol(AgentId);
        await _db.SaveChangesAsync();
        Assert.Equal(400, await StatusOf(() =>
            _notes.SaveAsync(AgentId, 21, new SaveNoteRequest(101, "note"))));
    }

    [Fact]
    public async Task Agents_only_ever_see_their_own_notes()
    {
        Enrol(AgentId);
        Enrol(OtherAgentId);
        await _db.SaveChangesAsync();
        await _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "mine"));
        await _notes.SaveAsync(OtherAgentId, 11, new SaveNoteRequest(101, "theirs"));

        var mine = Assert.Single(await _notes.GetCourseNotesAsync(AgentId, 101));
        Assert.Equal("mine", mine.Body);
    }

    // ── Saving ──

    [Fact]
    public async Task Any_item_type_takes_a_note_and_saving_again_replaces_it()
    {
        Enrol(AgentId);
        await _db.SaveChangesAsync();
        await _notes.SaveAsync(AgentId, 12, new SaveNoteRequest(101, "first"));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var saved = await _notes.SaveAsync(AgentId, 12, new SaveNoteRequest(101, "second"));

        Assert.Equal("second", saved!.Body);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, saved.UpdatedAt);
        Assert.Equal(1, await _db.Notes.CountAsync());
    }

    [Fact]
    public async Task Saving_an_empty_note_deletes_it()
    {
        Enrol(AgentId);
        await _db.SaveChangesAsync();
        await _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "note"));

        Assert.Null(await _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "   ")));
        Assert.Empty(await _db.Notes.ToListAsync());
    }

    [Fact]
    public async Task Oversized_notes_are_refused()
    {
        Enrol(AgentId);
        await _db.SaveChangesAsync();
        Assert.Equal(400, await StatusOf(() =>
            _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, new string('a', NoteService.MaxLength + 1)))));
    }

    // ── Lifetime ──

    [Fact]
    public async Task Unenrolling_deletes_that_courses_notes_only()
    {
        Enrol(AgentId);
        Enrol(AgentId, 102);
        await _db.SaveChangesAsync();
        await _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "course 101"));
        await _notes.SaveAsync(AgentId, 21, new SaveNoteRequest(102, "course 102"));

        await new AssignmentService(_db).UnassignBulkAsync(new BulkAssignmentRequest(101, [AgentId]));

        var left = Assert.Single(await _db.Notes.ToListAsync());
        Assert.Equal(102, left.CourseId);
    }

    [Fact]
    public async Task Deleting_an_item_deletes_its_notes()
    {
        Enrol(AgentId);
        await _db.SaveChangesAsync();
        await _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "note"));

        await new CourseService(_db).DeleteItemAsync(11);

        Assert.Empty(await _db.Notes.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_course_deletes_its_notes()
    {
        Enrol(AgentId);
        await _db.SaveChangesAsync();
        await _notes.SaveAsync(AgentId, 11, new SaveNoteRequest(101, "note"));

        await new CourseService(_db).DeleteAsync(101);

        Assert.Empty(await _db.Notes.ToListAsync());
    }

    public void Dispose() => _db.Dispose();
}

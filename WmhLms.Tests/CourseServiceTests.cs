using WmhLms.Core.Dtos;
using WmhLms.Core.Services;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Tests;

/// <summary>
/// Publishing rules and the lightweight catalogue list.
/// </summary>
public class CourseServiceTests : IDisposable
{
    private readonly AppDbContext _db = TestDb.Create();
    private readonly CourseService _courses;

    public CourseServiceTests()
    {
        _courses = new CourseService(_db);
        _db.Courses.Add(new Course
        {
            Id = 101, Title = "Course", Description = "", Status = "draft",
            Sections =
            [
                new Section { Id = 1, CourseId = 101, Title = "S1", Order = 1,
                    Items =
                    [
                        new Item { Id = 11, SectionId = 1, Title = "Text", Type = "text", Order = 1 },
                        new Item { Id = 12, SectionId = 1, Title = "Final Check", Type = "quiz", Order = 2 }
                    ] },
                new Section { Id = 2, CourseId = 101, Title = "S2", Order = 2,
                    Items = [new Item { Id = 21, SectionId = 2, Title = "Video", Type = "video", Order = 1 }] }
            ]
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task A_course_with_an_empty_quiz_cannot_be_published()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _courses.UpdateAsync(101, new UpdateCourseRequest(null, null, "published")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Final Check", ex.Message);
        Assert.Equal("draft", (await _db.Courses.FindAsync(101L))!.Status);
    }

    [Fact]
    public async Task Once_the_quiz_has_a_question_the_course_publishes()
    {
        await _courses.AddQuestionAsync(12, new QuestionRequest("true_false", "Q",
            [new OptionRequest(null, "True", true), new OptionRequest(null, "False", false)]));

        var course = await _courses.UpdateAsync(101, new UpdateCourseRequest(null, null, "published"));

        Assert.Equal("published", course.Status);
    }

    [Fact]
    public async Task Moving_back_to_draft_is_always_allowed()
    {
        var course = await _courses.UpdateAsync(101, new UpdateCourseRequest(null, null, "draft"));
        Assert.Equal("draft", course.Status);
    }

    [Fact]
    public async Task The_catalogue_list_returns_counts_rather_than_the_tree()
    {
        var page = await _courses.ListAsync(null, null, 1, 10);

        var row = Assert.Single(page.Data);
        Assert.Equal(2, row.SectionsCount);
        Assert.Equal(3, row.ItemsCount);
    }

    public void Dispose() => _db.Dispose();
}

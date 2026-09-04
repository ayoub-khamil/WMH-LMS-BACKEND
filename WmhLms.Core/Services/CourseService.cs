using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Dtos;
using WmhLms.Data;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

public class CourseService(AppDbContext db)
{
    private static readonly string[] Statuses = ["draft", "published"];
    private static readonly string[] ItemTypes = ["video", "text", "quiz", "audio"];
    private static readonly string[] QuestionTypes = ["multiple_choice", "true_false", "multiple_answer"];
    private static readonly string[] SingleAnswerTypes = ["multiple_choice", "true_false"];

    private IQueryable<Course> Full() => db.Courses
        .Include(c => c.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Questions)
        .ThenInclude(q => q.Options);

    public async Task<PagedResult<CourseDto>> ListAsync(string? status, string? search, int page, int limit)
    {
        (page, limit) = Guard.Paging(page, limit);
        var q = Full().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            q = q.Where(c => c.Title.ToLower().Contains(s)
                || (c.Description ?? "").ToLower().Contains(s));
        }
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(c => c.Id)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        return PagedResult<CourseDto>.Of(items.Select(Mapping.ToDto).ToList(), page, limit, total);
    }

    public async Task<CourseDto> GetByIdAsync(long id) =>
        Mapping.ToDto(await Full().FirstOrDefaultAsync(c => c.Id == id)
            ?? throw ApiException.NotFound("Course not found"));

    public async Task<CourseDto> CreateAsync(CreateCourseRequest req)
    {
        var course = new Course
        {
            Title = Guard.RequiredText(req.Title, "Title", 300),
            Description = (req.Description ?? "").Trim(),
            Status = "draft",
            CreatedAt = DateTime.UtcNow
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return await GetByIdAsync(course.Id);
    }

    public async Task<CourseDto> UpdateAsync(long id, UpdateCourseRequest req)
    {
        var course = await db.Courses.FindAsync(id) ?? throw ApiException.NotFound("Course not found");
        if (req.Title is not null) course.Title = Guard.RequiredText(req.Title, "Title", 300);
        if (req.Description is not null) course.Description = req.Description.Trim();
        if (req.Status is not null) course.Status = Guard.OneOf(req.Status, Statuses, "status");
        await db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(long id)
    {
        var course = await db.Courses.FindAsync(id) ?? throw ApiException.NotFound("Course not found");
        // Explicit rather than relying on the FK cascade, so the behaviour is
        // identical on any provider and visible at the call site.
        db.QuizLocks.RemoveRange(db.QuizLocks.Where(l => l.CourseId == id));
        db.Assignments.RemoveRange(db.Assignments.Where(a => a.CourseId == id));
        db.Courses.Remove(course);
        await db.SaveChangesAsync();
    }

    // ── Sections ──

    public async Task<SectionDto> AddSectionAsync(long courseId, SectionTitleRequest req)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId))
            throw ApiException.NotFound("Course not found");
        var title = Guard.RequiredText(req.Title, "Title", 300);
        // Max+1 rather than Count+1: deleting a section must not hand the next
        // one an order value that already exists.
        var nextOrder = await db.Sections.Where(s => s.CourseId == courseId)
            .Select(s => (int?)s.Order).MaxAsync() ?? 0;
        var section = new Section { CourseId = courseId, Title = title, Order = nextOrder + 1 };
        db.Sections.Add(section);
        await db.SaveChangesAsync();
        return Mapping.ToDto(section);
    }

    public async Task<List<SectionDto>> ReorderSectionsAsync(long courseId, List<long> ids)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId))
            throw ApiException.NotFound("Course not found");
        var sections = await db.Sections.Where(s => s.CourseId == courseId).ToListAsync();
        var map = sections.ToDictionary(s => s.Id);
        foreach (var id in ids ?? [])
            if (!map.ContainsKey(id))
                throw ApiException.BadRequest("Section list does not match this course.");
        for (var i = 0; i < (ids?.Count ?? 0); i++) map[ids![i]].Order = i + 1;
        await db.SaveChangesAsync();
        return (await Full().FirstAsync(c => c.Id == courseId)).Sections
            .OrderBy(s => s.Order).Select(Mapping.ToDto).ToList();
    }

    public async Task UpdateSectionAsync(long sectionId, SectionTitleRequest req)
    {
        var section = await db.Sections.FindAsync(sectionId)
            ?? throw ApiException.NotFound("Section not found");
        section.Title = Guard.RequiredText(req.Title, "Title", 300);
        await db.SaveChangesAsync();
    }

    public async Task DeleteSectionAsync(long sectionId)
    {
        var section = await db.Sections.FindAsync(sectionId)
            ?? throw ApiException.NotFound("Section not found");
        db.Sections.Remove(section);
        await db.SaveChangesAsync();
    }

    // ── Items ──

    public async Task<ItemDto> AddItemAsync(long sectionId, CreateItemRequest req)
    {
        if (!await db.Sections.AnyAsync(s => s.Id == sectionId))
            throw ApiException.NotFound("Section not found");
        var title = Guard.RequiredText(req.Title, "Title", 300);
        var type = Guard.OneOf(req.Type, ItemTypes, "item type");
        var nextOrder = await db.Items.Where(i => i.SectionId == sectionId)
            .Select(i => (int?)i.Order).MaxAsync() ?? 0;
        var item = new Item
        {
            SectionId = sectionId, Title = title, Type = type,
            ContentUrl = req.ContentUrl ?? "", TextContent = req.TextContent ?? "",
            Order = nextOrder + 1
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return Mapping.ToDto(item);
    }

    public async Task ReorderItemsAsync(long sectionId, List<long> ids)
    {
        if (!await db.Sections.AnyAsync(s => s.Id == sectionId))
            throw ApiException.NotFound("Section not found");
        var items = await db.Items.Where(i => i.SectionId == sectionId).ToListAsync();
        var map = items.ToDictionary(i => i.Id);
        foreach (var id in ids ?? [])
            if (!map.ContainsKey(id))
                throw ApiException.BadRequest("Item list does not match this section.");
        for (var i = 0; i < (ids?.Count ?? 0); i++) map[ids![i]].Order = i + 1;
        await db.SaveChangesAsync();
    }

    public async Task<ItemDto> UpdateItemAsync(long itemId, UpdateItemRequest req)
    {
        var item = await db.Items.Include(i => i.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw ApiException.NotFound("Item not found");
        if (req.Title is not null) item.Title = Guard.RequiredText(req.Title, "Title", 300);
        if (req.Type is not null) item.Type = Guard.OneOf(req.Type, ItemTypes, "item type");
        if (req.ContentUrl is not null) item.ContentUrl = req.ContentUrl;
        if (req.TextContent is not null) item.TextContent = req.TextContent;
        await db.SaveChangesAsync();
        return Mapping.ToDto(item);
    }

    public async Task DeleteItemAsync(long itemId)
    {
        var item = await db.Items.FindAsync(itemId)
            ?? throw ApiException.NotFound("Item not found");
        db.QuizLocks.RemoveRange(db.QuizLocks.Where(l => l.QuizItemId == itemId));
        db.Items.Remove(item);
        await db.SaveChangesAsync();
    }

    // ── Questions ──

    /// <summary>
    /// Shared shape rules for a question payload. Single-answer types are held
    /// to exactly one correct option so grading (set equality) stays meaningful.
    /// </summary>
    private static List<Option> BuildOptions(string type, long questionId, List<OptionRequest>? options)
    {
        if (options is null || options.Count < 2)
            throw ApiException.BadRequest("At least two options with one correct answer are required.");
        var correct = options.Count(o => o.IsCorrect);
        if (correct == 0)
            throw ApiException.BadRequest("At least two options with one correct answer are required.");
        if (correct > 1 && SingleAnswerTypes.Contains(type))
            throw ApiException.BadRequest("This question type allows exactly one correct answer.");
        return options.Select(o => new Option
        {
            QuestionId = questionId,
            Text = Guard.RequiredText(o.Text, "Option text", 1000),
            IsCorrect = o.IsCorrect
        }).ToList();
    }

    public async Task<QuestionDto> AddQuestionAsync(long itemId, QuestionRequest req)
    {
        var item = await db.Items.FindAsync(itemId)
            ?? throw ApiException.NotFound("Quiz item not found");
        if (item.Type != "quiz")
            throw ApiException.BadRequest("Questions can only be added to quiz items.");
        var type = Guard.OneOf(req.Type, QuestionTypes, "question type");
        var prompt = Guard.RequiredText(req.Prompt, "Prompt", 2000);
        BuildOptions(type, 0, req.Options); // validate before writing anything

        var question = new Question { ItemId = itemId, Type = type, Prompt = prompt };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        db.Options.AddRange(BuildOptions(type, question.Id, req.Options));
        await db.SaveChangesAsync();
        return Mapping.ToDto(await db.Questions.Include(x => x.Options)
            .FirstAsync(x => x.Id == question.Id));
    }

    public async Task<QuestionDto> UpdateQuestionAsync(long questionId, QuestionRequest req)
    {
        var question = await db.Questions.Include(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == questionId)
            ?? throw ApiException.NotFound("Question not found");
        var type = Guard.OneOf(req.Type, QuestionTypes, "question type");
        var prompt = Guard.RequiredText(req.Prompt, "Prompt", 2000);
        var replacements = BuildOptions(type, questionId, req.Options);

        question.Type = type;
        question.Prompt = prompt;
        db.Options.RemoveRange(question.Options);
        db.Options.AddRange(replacements);
        await db.SaveChangesAsync();
        return Mapping.ToDto(await db.Questions.Include(x => x.Options)
            .FirstAsync(x => x.Id == questionId));
    }

    public async Task DeleteQuestionAsync(long questionId)
    {
        var question = await db.Questions.FindAsync(questionId)
            ?? throw ApiException.NotFound("Question not found");
        db.Questions.Remove(question);
        await db.SaveChangesAsync();
    }
}

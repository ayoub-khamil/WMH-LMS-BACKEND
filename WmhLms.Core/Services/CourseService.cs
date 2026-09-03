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

    private IQueryable<Course> Full() => db.Courses
        .Include(c => c.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Questions)
        .ThenInclude(q => q.Options);

    public async Task<PagedResult<CourseDto>> ListAsync(string? status, string? search, int page, int limit)
    {
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
        if (string.IsNullOrWhiteSpace(req.Title)) throw ApiException.BadRequest("Title is required.");
        var c = new Course
        {
            Title = req.Title.Trim(), Description = req.Description?.Trim() ?? "",
            Status = "draft", CreatedAt = DateTime.UtcNow
        };
        db.Courses.Add(c);
        await db.SaveChangesAsync();
        return await GetByIdAsync(c.Id);
    }

    public async Task<CourseDto> UpdateAsync(long id, UpdateCourseRequest req)
    {
        var c = await db.Courses.FindAsync(id) ?? throw ApiException.NotFound("Course not found");
        if (req.Title is not null) c.Title = req.Title;
        if (req.Description is not null) c.Description = req.Description;
        if (req.Status is not null)
        {
            if (!Statuses.Contains(req.Status)) throw ApiException.BadRequest("Invalid status.");
            c.Status = req.Status;
        }
        await db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(long id)
    {
        var c = await db.Courses.FindAsync(id) ?? throw ApiException.NotFound("Course not found");
        db.Assignments.RemoveRange(db.Assignments.Where(a => a.CourseId == id));
        db.Courses.Remove(c);
        await db.SaveChangesAsync();
    }

    public async Task<SectionDto> AddSectionAsync(long courseId, SectionTitleRequest req)
    {
        var c = await db.Courses.Include(x => x.Sections)
            .FirstOrDefaultAsync(x => x.Id == courseId)
            ?? throw ApiException.NotFound("Course not found");
        if (string.IsNullOrWhiteSpace(req.Title)) throw ApiException.BadRequest("Title is required.");
        var s = new Section
        {
            CourseId = courseId, Title = req.Title.Trim(),
            Order = c.Sections.Count + 1
        };
        db.Sections.Add(s);
        await db.SaveChangesAsync();
        return Mapping.ToDto(s);
    }

    public async Task<List<SectionDto>> ReorderSectionsAsync(long courseId, List<long> ids)
    {
        var sections = await db.Sections.Where(s => s.CourseId == courseId).ToListAsync();
        if (!await db.Courses.AnyAsync(c => c.Id == courseId))
            throw ApiException.NotFound("Course not found");
        var map = sections.ToDictionary(s => s.Id);
        for (var i = 0; i < ids.Count; i++)
            if (map.TryGetValue(ids[i], out var s)) s.Order = i + 1;
        await db.SaveChangesAsync();
        return (await Full().FirstAsync(c => c.Id == courseId)).Sections
            .OrderBy(s => s.Order).Select(Mapping.ToDto).ToList();
    }

    public async Task UpdateSectionAsync(long sectionId, SectionTitleRequest req)
    {
        var s = await db.Sections.FindAsync(sectionId)
            ?? throw ApiException.NotFound("Section not found");
        s.Title = req.Title;
        await db.SaveChangesAsync();
    }

    public async Task DeleteSectionAsync(long sectionId)
    {
        var s = await db.Sections.FindAsync(sectionId)
            ?? throw ApiException.NotFound("Section not found");
        db.Sections.Remove(s);
        await db.SaveChangesAsync();
    }

    public async Task<ItemDto> AddItemAsync(long sectionId, CreateItemRequest req)
    {
        if (!await db.Sections.AnyAsync(s => s.Id == sectionId))
            throw ApiException.NotFound("Section not found");
        if (string.IsNullOrWhiteSpace(req.Title)) throw ApiException.BadRequest("Title is required.");
        if (!ItemTypes.Contains(req.Type)) throw ApiException.BadRequest("Invalid item type.");
        var item = new Item
        {
            SectionId = sectionId, Title = req.Title.Trim(), Type = req.Type,
            ContentUrl = req.ContentUrl ?? "", TextContent = req.TextContent ?? "",
            Order = await db.Items.Where(i => i.SectionId == sectionId).CountAsync() + 1
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return Mapping.ToDto(item);
    }

    public async Task ReorderItemsAsync(long sectionId, List<long> ids)
    {
        var items = await db.Items.Where(i => i.SectionId == sectionId).ToListAsync();
        var map = items.ToDictionary(i => i.Id);
        for (var i = 0; i < ids.Count; i++)
            if (map.TryGetValue(ids[i], out var item)) item.Order = i + 1;
        await db.SaveChangesAsync();
    }

    public async Task<ItemDto> UpdateItemAsync(long itemId, UpdateItemRequest req)
    {
        var item = await db.Items.Include(i => i.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw ApiException.NotFound("Item not found");
        if (req.Title is not null) item.Title = req.Title;
        if (req.Type is not null)
        {
            if (!ItemTypes.Contains(req.Type)) throw ApiException.BadRequest("Invalid item type.");
            item.Type = req.Type;
        }
        if (req.ContentUrl is not null) item.ContentUrl = req.ContentUrl;
        if (req.TextContent is not null) item.TextContent = req.TextContent;
        await db.SaveChangesAsync();
        return Mapping.ToDto(item);
    }

    public async Task DeleteItemAsync(long itemId)
    {
        var item = await db.Items.FindAsync(itemId)
            ?? throw ApiException.NotFound("Item not found");
        db.Items.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<QuestionDto> AddQuestionAsync(long itemId, QuestionRequest req)
    {
        if (!await db.Items.AnyAsync(i => i.Id == itemId))
            throw ApiException.NotFound("Quiz item not found");
        if (!QuestionTypes.Contains(req.Type)) throw ApiException.BadRequest("Invalid question type.");
        if (string.IsNullOrWhiteSpace(req.Prompt)) throw ApiException.BadRequest("Prompt is required.");
        if (req.Options is null || req.Options.Count < 2 || !req.Options.Any(o => o.IsCorrect))
            throw ApiException.BadRequest("At least two options with one correct answer are required.");
        var q = new Question { ItemId = itemId, Type = req.Type, Prompt = req.Prompt.Trim() };
        db.Questions.Add(q);
        await db.SaveChangesAsync();
        foreach (var o in req.Options)
            db.Options.Add(new Option { QuestionId = q.Id, Text = o.Text, IsCorrect = o.IsCorrect });
        await db.SaveChangesAsync();
        return Mapping.ToDto(await db.Questions.Include(x => x.Options).FirstAsync(x => x.Id == q.Id));
    }

    public async Task<QuestionDto> UpdateQuestionAsync(long questionId, QuestionRequest req)
    {
        var q = await db.Questions.Include(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == questionId)
            ?? throw ApiException.NotFound("Question not found");
        if (!QuestionTypes.Contains(req.Type)) throw ApiException.BadRequest("Invalid question type.");
        q.Type = req.Type;
        q.Prompt = req.Prompt;
        db.Options.RemoveRange(q.Options);
        foreach (var o in req.Options)
            db.Options.Add(new Option { QuestionId = q.Id, Text = o.Text, IsCorrect = o.IsCorrect });
        await db.SaveChangesAsync();
        return Mapping.ToDto(await db.Questions.Include(x => x.Options).FirstAsync(x => x.Id == q.Id));
    }

    public async Task DeleteQuestionAsync(long questionId)
    {
        var q = await db.Questions.FindAsync(questionId)
            ?? throw ApiException.NotFound("Question not found");
        db.Questions.Remove(q);
        await db.SaveChangesAsync();
    }
}

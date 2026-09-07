using WmhLms.Core.Dtos;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

/// <summary>
/// Entity to learner-facing DTO. Separate from Mapping so the type system,
/// not discipline, keeps Option.IsCorrect out of every learn response.
/// </summary>
public static class LearnMapper
{
    public static LearnOptionDto ToDto(Option o) => new(o.Id, o.Text);

    public static LearnQuestionDto ToDto(Question q) => new(q.Id, q.ItemId, q.Type, q.Prompt,
        q.Options.Select(ToDto).ToList());

    public static LearnItemDto ToDto(Item i) => new(i.Id, i.SectionId, i.Title, i.Type,
        i.ContentUrl, i.TextContent, i.Questions.Select(ToDto).ToList());

    public static LearnSectionDto ToDto(Section s) => new(s.Id, s.CourseId, s.Title, s.Order,
        s.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).Select(ToDto).ToList());
}

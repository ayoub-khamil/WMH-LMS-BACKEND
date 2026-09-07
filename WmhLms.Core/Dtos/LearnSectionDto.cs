namespace WmhLms.Core.Dtos;

public record LearnSectionDto(long Id, long CourseId, string Title, int Order,
    List<LearnItemDto> Items);

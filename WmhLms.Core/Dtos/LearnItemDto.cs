namespace WmhLms.Core.Dtos;

public record LearnItemDto(long Id, long SectionId, string Title, string Type,
    string ContentUrl, string TextContent, List<LearnQuestionDto> Questions);

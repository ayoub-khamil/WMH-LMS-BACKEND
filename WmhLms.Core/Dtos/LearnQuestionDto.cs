namespace WmhLms.Core.Dtos;

public record LearnQuestionDto(long Id, long ItemId, string Type, string Prompt,
    List<LearnOptionDto> Options);

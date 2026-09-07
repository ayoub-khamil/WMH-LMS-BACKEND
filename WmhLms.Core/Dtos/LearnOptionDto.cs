namespace WmhLms.Core.Dtos;

/// <summary>
/// Learner-facing option. Deliberately has no IsCorrect: the answer key must
/// never leave the server through a learn endpoint. Managers use OptionDto.
/// </summary>
public record LearnOptionDto(long Id, string Text);

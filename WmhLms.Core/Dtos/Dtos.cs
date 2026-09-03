using System.Text.Json.Serialization;

namespace WmhLms.Core.Dtos;

// All property names serialize to snake_case globally (see Program.cs),
// except where an explicit name is required by the frontend contract.
public record UserDto(
    long Id,
    string FirstName,
    string LastName,
    string Name,
    string Email,
    string Role,
    string Status,
    bool IsRoot,
    DateTime CreatedAt);

public record PaginationDto(int Page, int Limit, int Total,
    [property: JsonPropertyName("totalPages")] int TotalPages);

public record PagedResult<T>(List<T> Data, PaginationDto Pagination)
{
    public static PagedResult<T> Of(List<T> data, int page, int limit, int total) =>
        new(data, new PaginationDto(page, limit, total, (int)Math.Ceiling(total / (double)limit)));
}

public record OptionDto(long Id, string Text, bool IsCorrect);
public record QuestionDto(long Id, long ItemId, string Type, string Prompt, List<OptionDto> Options);
public record ItemDto(long Id, long SectionId, string Title, string Type,
    string ContentUrl, string TextContent, List<QuestionDto> Questions);
public record SectionDto(long Id, long CourseId, string Title, int Order, List<ItemDto> Items);
public record CourseDto(long Id, string Title, string Description, string Status,
    DateTime CreatedAt, List<SectionDto> Sections);

// Learn shapes
public record CourseWithProgressDto(long Id, string Title, string Description, string Status,
    DateTime CreatedAt, List<SectionDto> Sections,
    int Progress, List<long> CompletedItemIds, int TotalItems,
    DateTime? AssignedAt, DateTime? CompletedAt, string AssignmentStatus);
public record LearnBucketsDto(List<CourseWithProgressDto> InProgress,
    List<CourseWithProgressDto> NotStarted, List<CourseWithProgressDto> Completed);
public record CourseTreeDto(long Id, string Title, string Description, string Status,
    DateTime CreatedAt, List<SectionDto> Sections,
    List<long> CompletedItemIds, string AssignmentStatus);

// Requests
public record LoginRequest(string Email, string Password);
public record CreateUserRequest(string FirstName, string LastName, string Email,
    string? Password, string Role = "agent");
public record UpdateUserRequest(string? FirstName, string? LastName, string? Email,
    string? Password, string? Role);
public record UpdateStatusRequest(string Status);
public record CreateCourseRequest(string Title, string? Description);
public record UpdateCourseRequest(string? Title, string? Description, string? Status);
public record SectionTitleRequest(string Title);
public record ReorderSectionsRequest(List<long> SectionIds);
public record CreateItemRequest(string Title, string Type, string? ContentUrl, string? TextContent);
public record UpdateItemRequest(string? Title, string? Type, string? ContentUrl, string? TextContent);
public record ReorderItemsRequest(List<long> ItemIds);
public record OptionRequest(long? Id, string Text, bool IsCorrect);
public record QuestionRequest(string Type, string Prompt, List<OptionRequest> Options);
public record BulkAssignmentRequest(long CourseId, List<long> AgentIds);
public record CompleteItemRequest(long ItemId, long CourseId, long AgentId);
public record QuizAnswerRequest(long QuestionId, List<long> SelectedOptionIds);
public record SubmitQuizRequest(long ItemId, long CourseId, long AgentId, List<QuizAnswerRequest> Answers);

// Quiz submit response mirrors the old contract plus server lock info.
public record QuizResultDto(bool Passed, string Score, int ScorePercentage,
    List<long>? IncorrectQuestionIds, int? LockedSecondsRemaining);

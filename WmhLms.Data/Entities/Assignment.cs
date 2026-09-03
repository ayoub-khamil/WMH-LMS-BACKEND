namespace WmhLms.Data.Entities;

public class Assignment
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public long AgentId { get; set; }
    public string CompletedItemIdsJson { get; set; } = "[]";
    public string Status { get; set; } = "not_started"; // not_started | in_progress | completed
    public DateTime AssignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class QuizLock
{
    public long Id { get; set; }
    public long AgentId { get; set; }
    public long QuizItemId { get; set; }
    public long CourseId { get; set; }
    public DateTime LockedUntilUtc { get; set; }
    public string ReviewedItemIdsJson { get; set; } = "[]";
    public string? LastResultJson { get; set; }
}

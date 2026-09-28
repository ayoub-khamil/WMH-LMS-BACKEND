namespace WmhLms.Data.Entities;

/// <summary>
/// An agent's private notepad for one course item. One per (agent, item).
/// Belongs to the enrolment: un-enrolling deletes it, and so does deleting the
/// agent, the course or the item. Never exposed to managers.
/// </summary>
public class Note
{
    public long Id { get; set; }
    public long AgentId { get; set; }
    public long CourseId { get; set; }
    public long ItemId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

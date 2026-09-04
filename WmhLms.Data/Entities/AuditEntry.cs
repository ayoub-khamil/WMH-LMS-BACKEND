namespace WmhLms.Data.Entities;

/// <summary>
/// Append-only record of security-relevant administrative actions.
/// This is compliance training software: "who disabled this account, and when"
/// has to be answerable after the fact.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    /// <summary>Account that performed the action. Null once that account is deleted.</summary>
    public long? ActorId { get; set; }

    /// <summary>Denormalised so the trail stays readable after the actor is gone.</summary>
    public string ActorEmail { get; set; } = "";

    /// <summary>Stable verb, e.g. user.created, user.disabled, user.role_changed.</summary>
    public string Action { get; set; } = "";

    /// <summary>Entity the action was performed on, e.g. "user" or "course".</summary>
    public string TargetType { get; set; } = "";
    public long? TargetId { get; set; }
    public string TargetLabel { get; set; } = "";

    /// <summary>Human-readable detail. Never contains credentials.</summary>
    public string Detail { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}

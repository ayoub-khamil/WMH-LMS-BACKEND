namespace WmhLms.Data.Entities;

public class User
{
    public long Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "agent"; // manager | agent
    public string Status { get; set; } = "active"; // active | disabled
    public bool IsRoot { get; set; } // seeded superuser: undeletable, only root manages managers
    public DateTime CreatedAt { get; set; }
}

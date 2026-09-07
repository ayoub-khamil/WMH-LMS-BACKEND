namespace WmhLms.Core.Options;

/// <summary>
/// Bound from the RootManager configuration section (RootManager__* variables
/// in production). The password is only used the first time the account is
/// created; changing it later does nothing.
/// </summary>
public sealed class RootManagerOptions
{
    public const string SectionName = "RootManager";

    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

using System.Text.Json;

namespace WmhLms.Core.Services;

public static class JsonIds
{
    public static List<long> Read(string json)
    {
        try { return JsonSerializer.Deserialize<List<long>>(json) ?? new(); }
        catch { return new(); }
    }
    public static string Write(IEnumerable<long> ids) => JsonSerializer.Serialize(ids.ToList());
}

public static class Progress
{
    public static int Percent(int completed, int total) =>
        total > 0 ? (int)Math.Round(completed / (double)total * 100) : 0;
}

/// <summary>Shared input guards so validation reads the same in every service.</summary>
public static class Guard
{
    public const int MaxPageSize = 200;
    public const int MinPasswordLength = 8;

    public static string RequiredText(string? value, string field, int maxLength)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) throw ApiException.BadRequest($"{field} is required.");
        if (trimmed.Length > maxLength)
            throw ApiException.BadRequest($"{field} must be {maxLength} characters or fewer.");
        return trimmed;
    }

    public static string Email(string? value)
    {
        var email = RequiredText(value, "Email", 256);
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1 || email.Contains(' ')
            || email.IndexOf('@', at + 1) >= 0 || !email[(at + 1)..].Contains('.'))
            throw ApiException.BadRequest("Enter a valid email address.");
        // Stored lower-case so the plain unique index on Users.Email is
        // case-insensitive in effect and look-ups can compare directly.
        return email.ToLowerInvariant();
    }

    /// <summary>
    /// Server-side password policy. The UI enforces the same rule, but the API
    /// is reachable without it.
    /// </summary>
    public static string Password(string? value)
    {
        var password = value ?? "";
        if (password.Length < MinPasswordLength)
            throw ApiException.BadRequest(
                $"Password must be at least {MinPasswordLength} characters.");
        if (password.Length > 256)
            throw ApiException.BadRequest("Password must be 256 characters or fewer.");
        return password;
    }

    public static string OneOf(string? value, string[] allowed, string field)
    {
        if (value is null || !allowed.Contains(value))
            throw ApiException.BadRequest($"Invalid {field}.");
        return value;
    }

    /// <summary>Clamps client-supplied paging so a stray ?limit=1000000 cannot sink the process.</summary>
    public static (int Page, int Limit) Paging(int page, int limit) =>
        (page <= 0 ? 1 : page, limit <= 0 ? 10 : Math.Min(limit, MaxPageSize));
}

public class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public static ApiException NotFound(string m = "Not found") => new(404, m);
    public static ApiException BadRequest(string m) => new(400, m);
    public static ApiException Unauthorized(string m = "Invalid email or password.") => new(401, m);
    public static ApiException Forbidden(string m = "Forbidden.") => new(403, m);
    public static ApiException Conflict(string m) => new(409, m);
    public static ApiException Locked(string m, int retryAfterSeconds) => new(423, m)
    {
        RetryAfterSeconds = retryAfterSeconds
    };
    public int? RetryAfterSeconds { get; private init; }
}

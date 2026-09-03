using System.Text.Json;
using WmhLms.Data.Entities;

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

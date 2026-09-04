using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

/// <summary>
/// Read-only view of the administrative audit trail. Root only: the trail
/// records what managers did, so managers should not be the ones reading it.
/// </summary>
[Route("api/audit"), Authorize(Roles = "manager")]
public class AuditController(AuditService audit, UserService users) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Recent([FromQuery] int limit = 100)
    {
        var caller = await users.RequireRootAsync(CurrentUserId);
        _ = caller;
        var entries = await audit.RecentAsync(limit);
        return Ok(entries.Select(e => new Dictionary<string, object?>
        {
            ["id"] = e.Id,
            ["actor_email"] = e.ActorEmail,
            ["action"] = e.Action,
            ["target_type"] = e.TargetType,
            ["target_id"] = e.TargetId,
            ["target_label"] = e.TargetLabel,
            ["detail"] = e.Detail,
            ["created_at"] = e.CreatedAt
        }).ToList());
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

/// <summary>
/// Agent learning surface. Every action resolves the acting agent from the
/// bearer token via <see cref="BaseController.ResolveAgentId"/> before the
/// service sees it, so a client-supplied agent_id can never widen access.
/// </summary>
[Route("api/learn"), Authorize]
public class LearnController(LearnService learn) : BaseController
{
    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses([FromQuery] long agent_id) =>
        Ok(await learn.GetCoursesAsync(ResolveAgentId(agent_id)));

    [HttpGet("courses/{id:long}")]
    public async Task<IActionResult> GetCourseTree(long id, [FromQuery] long agent_id) =>
        Ok(await learn.GetCourseTreeAsync(id, ResolveAgentId(agent_id), IsManager));

    [HttpGet("courses/{id:long}/resume")]
    public async Task<IActionResult> Resume(long id, [FromQuery] long agent_id) =>
        Ok(await learn.ResumeAsync(id, ResolveAgentId(agent_id), IsManager));

    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteItemRequest req) =>
        Ok(await learn.CompleteItemAsync(req with { AgentId = ResolveAgentId(req.AgentId) }));

    [HttpPost("quiz/submit")]
    public async Task<IActionResult> SubmitQuiz([FromBody] SubmitQuizRequest req) =>
        Ok(await learn.SubmitQuizAsync(req with { AgentId = ResolveAgentId(req.AgentId) }));

    // Server-side review gate + lock status (back the UI cooldown mirror).
    [HttpPost("views")]
    public async Task<IActionResult> RecordView([FromBody] RecordViewRequest req)
    {
        await learn.RecordItemViewAsync(ResolveAgentId(req.AgentId), req.CourseId, req.ViewedItemId);
        return Ok(new { success = true });
    }

    [HttpGet("quiz/lock")]
    public async Task<IActionResult> LockStatus([FromQuery] long agent_id, [FromQuery] long item_id) =>
        Ok(await learn.GetLockStatusAsync(ResolveAgentId(agent_id), item_id)
            ?? new Dictionary<string, object?> { ["can_retry"] = true });
}

public record RecordViewRequest(long AgentId, long CourseId, long ViewedItemId);

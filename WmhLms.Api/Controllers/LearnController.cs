using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

[Route("api/learn"), Authorize]
public class LearnController(LearnService learn) : BaseController
{
    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses([FromQuery] long agent_id) =>
        Ok(await learn.GetCoursesAsync(agent_id));

    [HttpGet("courses/{id:long}")]
    public async Task<IActionResult> GetCourseTree(long id, [FromQuery] long agent_id) =>
        Ok(await learn.GetCourseTreeAsync(id, agent_id));

    [HttpGet("courses/{id:long}/resume")]
    public async Task<IActionResult> Resume(long id, [FromQuery] long agent_id) =>
        Ok(await learn.ResumeAsync(id, agent_id));

    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteItemRequest req) =>
        Ok(await learn.CompleteItemAsync(req));

    [HttpPost("quiz/submit")]
    public async Task<IActionResult> SubmitQuiz([FromBody] SubmitQuizRequest req) =>
        Ok(await learn.SubmitQuizAsync(req));

    // Server-side review gate + lock status (back the UI cooldown mirror).
    [HttpPost("views")]
    public async Task<IActionResult> RecordView([FromBody] RecordViewRequest req)
    {
        await learn.RecordItemViewAsync(req.AgentId, req.CourseId, req.ViewedItemId);
        return Ok(new { success = true });
    }

    [HttpGet("quiz/lock")]
    public async Task<IActionResult> LockStatus([FromQuery] long agent_id, [FromQuery] long item_id) =>
        Ok(await learn.GetLockStatusAsync(agent_id, item_id)
            ?? new Dictionary<string, object?> { ["can_retry"] = true });
}

public record RecordViewRequest(long AgentId, long CourseId, long ViewedItemId);

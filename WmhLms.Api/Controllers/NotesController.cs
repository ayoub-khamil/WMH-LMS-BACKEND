using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

/// <summary>
/// An agent's private notes. There is deliberately no agent_id parameter and
/// no use of ResolveAgentId: notes are always the signed-in user's own, so no
/// role (manager included) can read or write anyone else's.
/// </summary>
[Route("api/learn"), Authorize]
public class NotesController(NoteService notes) : BaseController
{
    private long Me => CurrentUserId > 0
        ? CurrentUserId
        : throw ApiException.Unauthorized("Session expired.");

    [HttpGet("courses/{courseId:long}/notes")]
    public async Task<IActionResult> GetCourseNotes(long courseId) =>
        Ok(await notes.GetCourseNotesAsync(Me, courseId));

    /// <summary>Creates or replaces the note for one item. An empty body deletes it.</summary>
    [HttpPut("notes/{itemId:long}")]
    public async Task<IActionResult> Save(long itemId, [FromBody] SaveNoteRequest req)
    {
        var saved = await notes.SaveAsync(Me, itemId, req);
        return Ok(saved is null
            ? new Dictionary<string, object?> { ["item_id"] = itemId, ["deleted"] = true }
            : saved);
    }
}

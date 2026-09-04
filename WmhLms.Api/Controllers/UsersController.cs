using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

[Route("api/users"), Authorize(Roles = "manager")]
public class UsersController(UserService users) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? role, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 10) =>
        Ok(await users.ListAsync(role, status, search, page, limit));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req) =>
        Ok(await users.CreateAsync(CurrentUserId, req));

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserRequest req) =>
        Ok(await users.UpdateAsync(CurrentUserId, id, req));

    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateStatusRequest req) =>
        Ok(await users.UpdateStatusAsync(CurrentUserId, id, req.Status));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await users.DeleteAsync(CurrentUserId, id);
        return Ok(new { success = true });
    }

    [HttpGet("{id:long}/assignments")]
    public async Task<IActionResult> GetAssignments(long id) =>
        Ok(await users.GetAssignmentsAsync(id));
}

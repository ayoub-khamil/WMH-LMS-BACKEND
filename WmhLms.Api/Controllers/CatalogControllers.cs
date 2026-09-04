using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

[Authorize(Roles = "manager")]
public class CoursesController(CourseService courses) : BaseController
{
    [HttpGet("/api/courses")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int limit = 10) =>
        Ok(await courses.ListAsync(status, search, page, limit));

    [HttpGet("/api/courses/{id:long}")]
    public async Task<IActionResult> GetById(long id) => Ok(await courses.GetByIdAsync(id));

    [HttpPost("/api/courses")]
    public async Task<IActionResult> Create([FromBody] CreateCourseRequest req) =>
        Ok(await courses.CreateAsync(req));

    [HttpPatch("/api/courses/{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateCourseRequest req) =>
        Ok(await courses.UpdateAsync(id, req));

    [HttpDelete("/api/courses/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await courses.DeleteAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("/api/courses/{id:long}/sections")]
    public async Task<IActionResult> AddSection(long id, [FromBody] SectionTitleRequest req) =>
        Ok(await courses.AddSectionAsync(id, req));

    [HttpPut("/api/courses/{id:long}/sections/order")]
    public async Task<IActionResult> ReorderSections(long id, [FromBody] ReorderSectionsRequest req) =>
        Ok(await courses.ReorderSectionsAsync(id, req.SectionIds));
}

[Route("api/sections"), Authorize(Roles = "manager")]
public class SectionsController(CourseService courses) : BaseController
{
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SectionTitleRequest req)
    {
        await courses.UpdateSectionAsync(id, req);
        return Ok(new { success = true });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await courses.DeleteSectionAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("{id:long}/items")]
    public async Task<IActionResult> AddItem(long id, [FromBody] CreateItemRequest req) =>
        Ok(await courses.AddItemAsync(id, req));

    [HttpPut("{id:long}/items/order")]
    public async Task<IActionResult> Reorder(long id, [FromBody] ReorderItemsRequest req)
    {
        await courses.ReorderItemsAsync(id, req.ItemIds);
        return Ok(new { success = true });
    }
}

[Route("api/items"), Authorize(Roles = "manager")]
public class ItemsController(CourseService courses) : BaseController
{
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateItemRequest req) =>
        Ok(await courses.UpdateItemAsync(id, req));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await courses.DeleteItemAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("{id:long}/questions")]
    public async Task<IActionResult> AddQuestion(long id, [FromBody] QuestionRequest req) =>
        Ok(await courses.AddQuestionAsync(id, req));
}

[Route("api/questions"), Authorize(Roles = "manager")]
public class QuestionsController(CourseService courses) : BaseController
{
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] QuestionRequest req) =>
        Ok(await courses.UpdateQuestionAsync(id, req));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await courses.DeleteQuestionAsync(id);
        return Ok(new { success = true });
    }
}

[Authorize(Roles = "manager")]
public class AssignmentsController(AssignmentService assignments) : BaseController
{
    [HttpPost("/api/assignments/bulk")]
    public async Task<IActionResult> Assign([FromBody] BulkAssignmentRequest req)
    {
        await assignments.AssignBulkAsync(req);
        return Ok(new { success = true });
    }

    [HttpDelete("/api/assignments/bulk")]
    public async Task<IActionResult> Unassign([FromBody] BulkAssignmentRequest req)
    {
        await assignments.UnassignBulkAsync(req);
        return Ok(new { success = true });
    }

    [HttpGet("/api/courses/{id:long}/assignments")]
    public async Task<IActionResult> GetCourseAssignments(long id) =>
        Ok(await assignments.GetCourseAssignmentsAsync(id));
}

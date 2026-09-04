using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WmhLms.Core.Dtos;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

[Route("api/auth")]
public class AuthController(AuthService auth) : BaseController
{
    // Credential stuffing guard: the limiter is keyed on client IP.
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(Program.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var (token, user) = await auth.LoginAsync(req.Email, req.Password);
        return Ok(new Dictionary<string, object?> { ["token"] = token, ["user"] = user });
    }

    [HttpPost("logout"), Authorize]
    public IActionResult Logout() => Ok(new { success = true });
}

[Route("api")]
public class MeController(AuthService auth) : BaseController
{
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me() =>
        Ok(await auth.MeAsync(CurrentUserId));

    [HttpGet("health"), AllowAnonymous]
    public IActionResult Health() => Ok(new { ok = true });
}

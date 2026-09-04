using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Services;

namespace WmhLms.Api.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    protected long CurrentUserId
    {
        get
        {
            // JwtSecurityTokenHandler remaps "sub" -> NameIdentifier by default.
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return long.TryParse(raw, out var id) ? id : 0;
        }
    }

    protected bool IsManager => User.IsInRole("manager");

    /// <summary>
    /// The learn endpoints take an agent_id from the client. That value is a
    /// hint, never an authority: an agent may only ever act as themselves.
    /// Managers keep the ability to read on an agent behalf (content preview
    /// and support), which is why the caller role is consulted rather than
    /// dropping the parameter outright.
    /// </summary>
    protected long ResolveAgentId(long requestedAgentId)
    {
        var me = CurrentUserId;
        if (me == 0) throw ApiException.Unauthorized("Session expired.");
        if (requestedAgentId <= 0 || requestedAgentId == me) return me;
        if (IsManager) return requestedAgentId;
        throw ApiException.Forbidden("You can only access your own learning data.");
    }
}

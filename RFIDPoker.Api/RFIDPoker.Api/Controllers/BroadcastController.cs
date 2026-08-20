using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

public record BroadcastStatusDto(bool IsLive);

[ApiController]
[Route("api/broadcast")]
[Authorize]
public class BroadcastController(IBroadcastState broadcast) : ControllerBase
{
    /// <summary>Anyone signed in can read the current state (needed by nav pill).</summary>
    [HttpGet]
    public ActionResult<BroadcastStatusDto> Get() => new BroadcastStatusDto(broadcast.IsLive);

    [HttpPost("start")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<ActionResult<BroadcastStatusDto>> Start(CancellationToken ct)
    {
        await broadcast.StartAsync(ct);
        return new BroadcastStatusDto(broadcast.IsLive);
    }

    [HttpPost("stop")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<ActionResult<BroadcastStatusDto>> Stop(CancellationToken ct)
    {
        await broadcast.StopAsync(ct);
        return new BroadcastStatusDto(broadcast.IsLive);
    }
}

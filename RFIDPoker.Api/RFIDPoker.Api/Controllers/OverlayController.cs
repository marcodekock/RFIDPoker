using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

/// <summary>
/// Read-only endpoints reachable ONLY with an overlay token. Any user JWT is rejected
/// by the OverlayRead policy, and the SignalR hub reuses the same policy for streamed
/// updates. The overlay must never be able to reach admin/state-mutating endpoints.
/// </summary>
[ApiController]
[Route("api/overlay")]
[Authorize(Policy = AuthPolicies.OverlayRead)]
public class OverlayController(IPokerAnalysisEngine analysis) : ControllerBase
{
    /// <summary>Full analysis snapshot the overlay renders (community, players, equity, break state).</summary>
    [HttpGet("state")]
    public ActionResult<AnalysisResultDto> GetState()
    {
        var snap = analysis.GetLatestResult();
        return snap is null ? NoContent() : Ok(snap);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.RequireUser)]
public class TournamentController(
    ITournamentStateManager tournament,
    ITableStateManager tableState) : ControllerBase
{
    /// <summary>Snapshot of the current break, or null when no break is active.</summary>
    [HttpGet("break")]
    public ActionResult<BreakStateDto?> GetBreak() => Ok(tournament.GetBreakSnapshot());

    /// <summary>Starts (or restarts) a break for the given duration in seconds.</summary>
    [HttpPost("break/start")]
    public ActionResult<BreakStateDto> StartBreak([FromBody] StartBreakRequest request)
    {
        if (request is null || request.DurationSeconds < 1)
            return BadRequest("DurationSeconds must be at least 1.");
        return Ok(tournament.StartBreak(request.DurationSeconds, request.Label));
    }

    [HttpPost("break/pause")]
    public ActionResult<BreakStateDto?> Pause() => Ok(tournament.PauseBreak());

    [HttpPost("break/resume")]
    public ActionResult<BreakStateDto?> Resume() => Ok(tournament.ResumeBreak());

    [HttpPost("break/adjust")]
    public ActionResult<BreakStateDto?> Adjust([FromBody] AdjustBreakRequest request)
    {
        if (request is null) return BadRequest("Body required.");
        return Ok(tournament.AdjustBreak(request.DeltaSeconds));
    }

    [HttpPost("break/stop")]
    public IActionResult Stop()
    {
        tournament.StopBreak();
        return NoContent();
    }

    /// <summary>Convenience: manually trigger a new hand (clears community/mucked/hole cards).</summary>
    [HttpPost("new-hand")]
    public IActionResult NewHand()
    {
        tableState.NewHand();
        return NoContent();
    }
}

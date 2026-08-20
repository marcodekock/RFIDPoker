using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

public record TournamentDirectorSettingsDto(bool Enabled);
public record TournamentDirectorTokenGeneratedDto(string Token);

public record TournamentDirectorAdminStatusDto(
    bool Enabled,
    bool HasActiveToken,
    DateTimeOffset? TokenCreatedAt,
    DateTimeOffset? LastUpdatedUtc,
    TournamentDirectorUpdate? Latest);

[ApiController]
[Route("api/tournament-director")]
public class TournamentDirectorController(
    ITournamentDirectorTokenService tokenSvc,
    ITournamentDirectorState state,
    ISettingsStore settings) : ControllerBase
{
    // --- Admin endpoints --------------------------------------------------------

    [HttpGet("status")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<TournamentDirectorAdminStatusDto> Status(CancellationToken ct)
    {
        var s = await tokenSvc.GetStatusAsync(ct);
        return new TournamentDirectorAdminStatusDto(
            Enabled: state.IsEnabled,
            HasActiveToken: s.HasActiveToken,
            TokenCreatedAt: s.CreatedAt,
            LastUpdatedUtc: state.LastUpdatedUtc,
            Latest: state.Latest);
    }

    [HttpPost("generate")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<ActionResult<TournamentDirectorTokenGeneratedDto>> Generate(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var gen = await tokenSvc.GenerateAsync(userId, ct);
        return new TournamentDirectorTokenGeneratedDto(gen.RawToken);
    }

    [HttpPost("revoke")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        await tokenSvc.RevokeAsync(ct);
        return NoContent();
    }

    [HttpGet("settings")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<TournamentDirectorSettingsDto> GetSettings(CancellationToken ct)
    {
        var enabled = await settings.GetAsync(SettingKeys.TournamentDirectorEnabled, false, ct);
        state.SetEnabled(enabled); // keep in-memory flag in sync
        return new TournamentDirectorSettingsDto(enabled);
    }

    [HttpPut("settings")]
    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    public async Task<ActionResult<TournamentDirectorSettingsDto>> UpdateSettings(
        [FromBody] TournamentDirectorSettingsDto input, CancellationToken ct)
    {
        if (input.Enabled)
        {
            var status = await tokenSvc.GetStatusAsync(ct);
            if (!status.HasActiveToken)
                return BadRequest(new { message = "Generate a Tournament Director token before enabling the integration." });
        }
        await settings.SetAsync(SettingKeys.TournamentDirectorEnabled, input.Enabled, ct);
        state.SetEnabled(input.Enabled);
        return new TournamentDirectorSettingsDto(input.Enabled);
    }

    // --- Webhook endpoint -------------------------------------------------------

    /// <summary>
    /// Called by Tournament Director on every tick. Authenticated via a raw token
    /// passed as <c>?token=</c> query parameter (TD cannot send custom headers).
    /// Header <c>X-TD-Token</c> also accepted for callers that can send headers.
    /// Ignored silently when the integration is disabled so operators can leave TD
    /// configured to point here without side effects.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook([FromBody] TournamentDirectorUpdate update, CancellationToken ct)
    {
        // Prefer the query-string token because TD cannot add custom headers.
        var token = Request.Query["token"].ToString();
        if (string.IsNullOrEmpty(token))
            token = Request.Headers["X-TD-Token"].ToString();

        if (string.IsNullOrWhiteSpace(token) || !await tokenSvc.ValidateAsync(token, ct))
            return Unauthorized();

        if (!state.IsEnabled)
            return Ok(new { accepted = false, reason = "Tournament Director integration disabled." });

        state.Apply(update);
        return Ok(new { accepted = true });
    }
}

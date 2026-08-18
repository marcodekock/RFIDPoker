using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Auth;

namespace RFIDPoker.Api.Controllers;

public class OverlayTokenSettings
{
    public int DefaultLifetimeHours { get; set; } = 24;
    public int MaxLifetimeHours { get; set; } = 24 * 30;
}

public record GenerateOverlayTokenRequest(int? LifetimeHours);
public record OverlayTokenGeneratedDto(string Token, DateTimeOffset ExpiresAt);

[ApiController]
[Route("api/overlay-token")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class OverlayTokenController(
    IOverlayTokenService svc,
    IOptions<OverlayTokenSettings> settings) : ControllerBase
{
    [HttpGet("status")]
    public Task<OverlayTokenStatus> Status(CancellationToken ct) => svc.GetStatusAsync(ct);

    [HttpPost("generate")]
    public async Task<ActionResult<OverlayTokenGeneratedDto>> Generate(
        [FromBody] GenerateOverlayTokenRequest req, CancellationToken ct)
    {
        var cfg = settings.Value;
        var hours = req.LifetimeHours ?? cfg.DefaultLifetimeHours;
        if (hours < 1) hours = 1;
        if (hours > cfg.MaxLifetimeHours) hours = cfg.MaxLifetimeHours;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var gen = await svc.GenerateAsync(TimeSpan.FromHours(hours), userId, ct);
        return new OverlayTokenGeneratedDto(gen.RawToken, gen.ExpiresAt);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        await svc.RevokeAsync(ct);
        return NoContent();
    }
}

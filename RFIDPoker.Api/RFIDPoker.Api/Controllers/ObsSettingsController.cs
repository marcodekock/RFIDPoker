using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/obs")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class ObsSettingsController(
    ISettingsStore store,
    IOptions<ObsSettings> bootstrapDefaults) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ObsSettings>> Get(CancellationToken ct)
    {
        var current = await store.GetAsync(SettingKeys.Obs, bootstrapDefaults.Value, ct);
        // Never leak the password to the client — return a masked placeholder.
        return new ObsSettings
        {
            Enabled = current.Enabled,
            WebSocketUrl = current.WebSocketUrl,
            Password = string.IsNullOrEmpty(current.Password) ? "" : "********",
            ReconnectDelayMs = current.ReconnectDelayMs,
            SecondaryRotationSeconds = current.SecondaryRotationSeconds,
            SwitchDebounceMs = current.SwitchDebounceMs
        };
    }

    [HttpPut]
    public async Task<ActionResult<ObsSettings>> Put([FromBody] ObsSettings input, CancellationToken ct)
    {
        var current = await store.GetAsync(SettingKeys.Obs, bootstrapDefaults.Value, ct);
        // Empty-or-masked password means "keep existing" — otherwise take the new value.
        var newPassword = (string.IsNullOrEmpty(input.Password) || input.Password == "********")
            ? current.Password
            : input.Password;

        var merged = new ObsSettings
        {
            Enabled = input.Enabled,
            WebSocketUrl = string.IsNullOrWhiteSpace(input.WebSocketUrl) ? current.WebSocketUrl : input.WebSocketUrl.Trim(),
            Password = newPassword,
            ReconnectDelayMs = Math.Max(500, input.ReconnectDelayMs),
            SecondaryRotationSeconds = Math.Max(1, input.SecondaryRotationSeconds),
            SwitchDebounceMs = Math.Max(0, input.SwitchDebounceMs)
        };
        await store.SetAsync(SettingKeys.Obs, merged, ct);
        return await Get(ct);
    }
}

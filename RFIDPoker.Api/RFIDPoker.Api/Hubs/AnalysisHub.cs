using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RFIDPoker.Api.Auth;

namespace RFIDPoker.Api.Hubs;

/// <summary>
/// Broadcasts poker analysis snapshots to authenticated clients. Accepts both user
/// JWTs (Angular UI) and overlay JWTs (OBS). The hub has no invocable methods —
/// overlay tokens can therefore only *receive* broadcasts, never mutate state.
/// </summary>
[Authorize(Policy = AuthPolicies.UserOrOverlay)]
public class AnalysisHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}

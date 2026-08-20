using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

public record ManualTournamentInfoDto(
    int Level,
    int PlayersLeft,
    long TotalChips,
    int SmallBlind,
    int BigBlind,
    int NextSmallBlind,
    int NextBigBlind);

[ApiController]
[Route("api/tournament/manual-info")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class ManualTournamentController(
    IManualTournamentState state,
    ISettingsStore settings) : ControllerBase
{
    [HttpGet]
    public ManualTournamentInfoDto Get()
    {
        var c = state.Current;
        return new ManualTournamentInfoDto(
            c.Level, c.PlayersLeft, c.TotalChips,
            c.SmallBlind, c.BigBlind,
            c.NextSmallBlind, c.NextBigBlind);
    }

    [HttpPut]
    public async Task<ManualTournamentInfoDto> Update([FromBody] ManualTournamentInfoDto input, CancellationToken ct)
    {
        var value = new ManualTournamentInfo
        {
            Level = Math.Max(0, input.Level),
            PlayersLeft = Math.Max(0, input.PlayersLeft),
            TotalChips = Math.Max(0L, input.TotalChips),
            SmallBlind = Math.Max(0, input.SmallBlind),
            BigBlind = Math.Max(0, input.BigBlind),
            NextSmallBlind = Math.Max(0, input.NextSmallBlind),
            NextBigBlind = Math.Max(0, input.NextBigBlind)
        };
        await settings.SetAsync(SettingKeys.ManualTournamentInfo, value, ct);
        state.Set(value);
        return new ManualTournamentInfoDto(
            value.Level, value.PlayersLeft, value.TotalChips,
            value.SmallBlind, value.BigBlind,
            value.NextSmallBlind, value.NextBigBlind);
    }
}

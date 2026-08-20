using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Dtos;

public record PlayerAnalysisDto
{
    public int SeatNumber { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public long? ChipCount { get; init; }
    public List<Card> HoleCards { get; init; } = [];
    public HandRank? HandRank { get; init; }
    public string HandDescription { get; init; } = string.Empty;
    public List<Card> BestFiveCards { get; init; } = [];
    public int WinPercentage { get; init; }
    public int TiePercentage { get; init; }
    public int LosePercentage { get; init; }
    public bool IsFolded { get; init; }
}

public record AnalysisResultDto
{
    public Street CurrentStreet { get; init; }
    public string? Blinds { get; init; }
    public List<Card> CommunityCards { get; init; } = [];
    public List<Card> MuckedCards { get; init; } = [];
    public List<PlayerAnalysisDto> ActivePlayers { get; init; } = [];
    public List<PlayerAnalysisDto> FoldedPlayers { get; init; } = [];
    public int ActivePlayerCount { get; init; }
    public HeadsUpOutsDto? HeadsUpOuts { get; init; }
    public BreakStateDto? Break { get; init; }
    public TournamentDirectorSnapshotDto? Tournament { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Snapshot of tournament-wide state sourced from Tournament Director (when enabled).
/// Present only when the TD integration is active — front-end should treat these
/// values as authoritative and hide the manual editors.
/// </summary>
public record TournamentDirectorSnapshotDto
{
    public int Level { get; init; }
    public int PlayersLeft { get; init; }
    public long TotalChips { get; init; }
    public long AverageStack { get; init; }
    public int SmallBlind { get; init; }
    public int BigBlind { get; init; }
    public int NextSmallBlind { get; init; }
    public int NextBigBlind { get; init; }
    public bool IsBreak { get; init; }
    public bool NextIsBreak { get; init; }
    public int SecondsLeft { get; init; }
    public int LevelDuration { get; init; }
    public bool ClockPaused { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
}

public record HeadsUpOutsDto
{
    public int SeatNumber { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public List<Card> Outs { get; init; } = [];
}

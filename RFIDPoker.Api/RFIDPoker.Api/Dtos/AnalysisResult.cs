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
    public DateTimeOffset Timestamp { get; init; }
}

public record HeadsUpOutsDto
{
    public int SeatNumber { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public List<Card> Outs { get; init; } = [];
}

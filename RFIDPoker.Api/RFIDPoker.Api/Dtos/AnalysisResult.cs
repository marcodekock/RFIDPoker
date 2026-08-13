using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Dtos;

public record PlayerAnalysisDto
{
    public int SeatNumber { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public List<Card> HoleCards { get; init; } = [];
    public HandRank? HandRank { get; init; }
    public string HandDescription { get; init; } = string.Empty;
    public List<Card> BestFiveCards { get; init; } = [];
    public double WinPercentage { get; init; }
    public double TiePercentage { get; init; }
    public double LosePercentage { get; init; }
    public bool IsFolded { get; init; }
    public bool IsDealer { get; init; }
}

public record AnalysisResultDto
{
    public Street CurrentStreet { get; init; }
    public List<Card> CommunityCards { get; init; } = [];
    public List<PlayerAnalysisDto> ActivePlayers { get; init; } = [];
    public List<PlayerAnalysisDto> FoldedPlayers { get; init; } = [];
    public int ActivePlayerCount { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

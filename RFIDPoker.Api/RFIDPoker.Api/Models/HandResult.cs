namespace RFIDPoker.Api.Models;

public record HandResult(
    HandRank Rank,
    string Description,
    List<Card> BestFiveCards,
    List<Card> Kickers);

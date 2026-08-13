using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public record EquityResult(double WinPercentage, double TiePercentage, double LosePercentage);

public interface IEquityCalculator
{
    Task<Dictionary<int, EquityResult>> CalculateEquityAsync(
        List<Player> activePlayers,
        List<Card> communityCards,
        int iterations = 100_000,
        CancellationToken cancellationToken = default);
}

public class EquityCalculator(IHandEvaluator handEvaluator) : IEquityCalculator
{
    public Task<Dictionary<int, EquityResult>> CalculateEquityAsync(
        List<Player> activePlayers,
        List<Card> communityCards,
        int iterations = 100_000,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Calculate(activePlayers, communityCards, iterations, cancellationToken), cancellationToken);
    }

    private Dictionary<int, EquityResult> Calculate(
        List<Player> activePlayers,
        List<Card> communityCards,
        int iterations,
        CancellationToken ct)
    {
        var wins = new Dictionary<int, int>();
        var ties = new Dictionary<int, int>();

        foreach (var p in activePlayers)
        {
            wins[p.SeatNumber] = 0;
            ties[p.SeatNumber] = 0;
        }

        // If all community cards are dealt (river), do exact evaluation
        if (communityCards.Count == 5)
        {
            return CalculateExact(activePlayers, communityCards);
        }

        var usedCards = new HashSet<Card>(
            communityCards.Concat(activePlayers.SelectMany(p => p.HoleCards)));

        var deck = BuildDeck().Where(c => !usedCards.Contains(c)).ToList();
        var cardsNeeded = 5 - communityCards.Count;
        var random = new Random();

        for (int i = 0; i < iterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var simBoard = new List<Card>(communityCards);
            Shuffle(deck, random, cardsNeeded);
            for (int j = 0; j < cardsNeeded; j++)
                simBoard.Add(deck[j]);

            EvaluateWinners(activePlayers, simBoard, wins, ties);
        }

        var results = new Dictionary<int, EquityResult>();
        foreach (var p in activePlayers)
        {
            double w = (double)wins[p.SeatNumber] / iterations * 100;
            double t = (double)ties[p.SeatNumber] / iterations * 100;
            results[p.SeatNumber] = new EquityResult(w, t, 100 - w - t);
        }

        return results;
    }

    private Dictionary<int, EquityResult> CalculateExact(List<Player> activePlayers, List<Card> communityCards)
    {
        var wins = new Dictionary<int, int>();
        var ties = new Dictionary<int, int>();
        foreach (var p in activePlayers)
        {
            wins[p.SeatNumber] = 0;
            ties[p.SeatNumber] = 0;
        }

        EvaluateWinners(activePlayers, communityCards, wins, ties);

        var results = new Dictionary<int, EquityResult>();
        foreach (var p in activePlayers)
        {
            double w = wins[p.SeatNumber] * 100.0;
            double t = ties[p.SeatNumber] * 100.0;
            results[p.SeatNumber] = new EquityResult(w, t, 100 - w - t);
        }

        return results;
    }

    private void EvaluateWinners(List<Player> players, List<Card> board,
        Dictionary<int, int> wins, Dictionary<int, int> ties)
    {
        var hands = new List<(int Seat, HandResult Result)>();

        foreach (var p in players)
        {
            var result = handEvaluator.EvaluateBestHand(p.HoleCards, board);
            if (result is not null)
                hands.Add((p.SeatNumber, result));
        }

        if (hands.Count == 0) return;

        hands.Sort((a, b) => HandEvaluator.CompareHands(b.Result, a.Result));

        var bestResult = hands[0].Result;
        var winners = hands.Where(h => HandEvaluator.CompareHands(h.Result, bestResult) == 0).ToList();

        if (winners.Count == 1)
        {
            wins[winners[0].Seat]++;
        }
        else
        {
            foreach (var w in winners)
                ties[w.Seat]++;
        }
    }

    private static List<Card> BuildDeck()
    {
        var deck = new List<Card>(52);
        foreach (Suit suit in Enum.GetValues<Suit>())
        foreach (Rank rank in Enum.GetValues<Rank>())
            deck.Add(new Card(rank, suit));
        return deck;
    }

    private static void Shuffle(List<Card> deck, Random random, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int j = random.Next(i, deck.Count);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }
}

using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public record EquityResult(double WinPercentage, double TiePercentage, double LosePercentage);

public interface IEquityCalculator
{
    Task<Dictionary<int, EquityResult>> CalculateEquityAsync(
        List<Player> activePlayers,
        List<Card> communityCards,
        IEnumerable<Card>? deadCards = null,
        int iterations = 0,
        CancellationToken cancellationToken = default);
}

public class EquityCalculator(IHandEvaluator handEvaluator) : IEquityCalculator
{
    public Task<Dictionary<int, EquityResult>> CalculateEquityAsync(
        List<Player> activePlayers,
        List<Card> communityCards,
        IEnumerable<Card>? deadCards = null,
        int iterations = 0,
        CancellationToken cancellationToken = default)
    {
        // Adaptive iteration count by street: preflop needs the most samples,
        // and each additional community card sharply reduces variance.
        if (iterations <= 0)
        {
            iterations = communityCards.Count switch
            {
                0 => 10_000, // preflop
                3 => 5_000,  // flop
                4 => 2_000,  // turn
                _ => 1_000
            };
        }

        var dead = deadCards?.ToList() ?? [];
        return Task.Run(() => Calculate(activePlayers, communityCards, dead, iterations, cancellationToken), cancellationToken);
    }

    private Dictionary<int, EquityResult> Calculate(
        List<Player> activePlayers,
        List<Card> communityCards,
        List<Card> deadCards,
        int iterations,
        CancellationToken ct)
    {
        // If all community cards are dealt (river), do exact evaluation
        if (communityCards.Count == 5)
        {
            return CalculateExact(activePlayers, communityCards);
        }

        // Cards that must not be dealt onto the simulated board:
        //   - already on the board
        //   - held by any active player (including hole cards not yet dealt-out here)
        //   - held by folded/mucked players (their cards are dead but still off the deck)
        var usedCards = new HashSet<Card>(
            communityCards
                .Concat(activePlayers.SelectMany(p => p.HoleCards))
                .Concat(deadCards));

        var deckTemplate = BuildDeck().Where(c => !usedCards.Contains(c)).ToArray();
        var cardsNeeded = 5 - communityCards.Count;
        var seats = activePlayers.Select(p => p.SeatNumber).ToArray();
        var seatIndex = seats
            .Select((s, i) => (s, i))
            .ToDictionary(x => x.s, x => x.i);

        var totalWins = new int[seats.Length];
        var totalTies = new int[seats.Length];

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        Parallel.For(0, iterations, parallelOptions,
            () => new ThreadLocalState(deckTemplate, seats.Length),
            (i, _, state) =>
            {
                state.ShuffleFirst(cardsNeeded);

                // Build the simulated board without allocating a new list each iteration.
                state.SimBoard.Clear();
                state.SimBoard.AddRange(communityCards);
                for (int j = 0; j < cardsNeeded; j++)
                    state.SimBoard.Add(state.Deck[j]);

                EvaluateWinners(activePlayers, state.SimBoard, seatIndex, state.Wins, state.Ties);
                return state;
            },
            state =>
            {
                lock (totalWins)
                {
                    for (int i = 0; i < seats.Length; i++)
                    {
                        totalWins[i] += state.Wins[i];
                        totalTies[i] += state.Ties[i];
                    }
                }
            });

        var results = new Dictionary<int, EquityResult>();
        for (int i = 0; i < seats.Length; i++)
        {
            double w = (double)totalWins[i] / iterations * 100;
            double t = (double)totalTies[i] / iterations * 100;
            results[seats[i]] = new EquityResult(w, t, 100 - w - t);
        }

        return results;
    }

    private Dictionary<int, EquityResult> CalculateExact(List<Player> activePlayers, List<Card> communityCards)
    {
        var seats = activePlayers.Select(p => p.SeatNumber).ToArray();
        var seatIndex = seats
            .Select((s, i) => (s, i))
            .ToDictionary(x => x.s, x => x.i);

        var wins = new int[seats.Length];
        var ties = new int[seats.Length];

        EvaluateWinners(activePlayers, communityCards, seatIndex, wins, ties);

        var results = new Dictionary<int, EquityResult>();
        for (int i = 0; i < seats.Length; i++)
        {
            double w = wins[i] * 100.0;
            double t = ties[i] * 100.0;
            results[seats[i]] = new EquityResult(w, t, 100 - w - t);
        }

        return results;
    }

    private void EvaluateWinners(List<Player> players, List<Card> board,
        Dictionary<int, int> seatIndex, int[] wins, int[] ties)
    {
        HandResult? bestResult = null;
        int bestSeatIdx = -1;
        int tieCount = 0;
        Span<int> tiedIndices = stackalloc int[players.Count];

        foreach (var p in players)
        {
            var result = handEvaluator.EvaluateBestHand(p.HoleCards, board);
            if (result is null) continue;

            var idx = seatIndex[p.SeatNumber];

            if (bestResult is null)
            {
                bestResult = result;
                bestSeatIdx = idx;
                tieCount = 0;
                continue;
            }

            var cmp = HandEvaluator.CompareHands(result, bestResult);
            if (cmp > 0)
            {
                bestResult = result;
                bestSeatIdx = idx;
                tieCount = 0;
            }
            else if (cmp == 0)
            {
                if (tieCount == 0)
                {
                    tiedIndices[tieCount++] = bestSeatIdx;
                }
                tiedIndices[tieCount++] = idx;
            }
        }

        if (bestResult is null) return;

        if (tieCount == 0)
        {
            wins[bestSeatIdx]++;
        }
        else
        {
            for (int i = 0; i < tieCount; i++)
                ties[tiedIndices[i]]++;
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

    private sealed class ThreadLocalState
    {
        public readonly Card[] Deck;
        public readonly List<Card> SimBoard;
        public readonly int[] Wins;
        public readonly int[] Ties;
        public readonly Random Rng;

        public ThreadLocalState(Card[] deckTemplate, int playerCount)
        {
            Deck = (Card[])deckTemplate.Clone();
            SimBoard = new List<Card>(5);
            Wins = new int[playerCount];
            Ties = new int[playerCount];
            // Random.Shared is thread-safe but slower per call; a per-thread instance is fastest.
            Rng = new Random(Guid.NewGuid().GetHashCode());
        }

        /// <summary>Partial Fisher-Yates: shuffles the first <paramref name="count"/> slots of the deck.</summary>
        public void ShuffleFirst(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int j = Rng.Next(i, Deck.Length);
                (Deck[i], Deck[j]) = (Deck[j], Deck[i]);
            }
        }
    }
}

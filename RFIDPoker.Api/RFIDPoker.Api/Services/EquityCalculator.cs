using HoldemPoker.Cards;
using HoldemPoker.Evaluator;
using RFIDPoker.Api.Models;
using HoldemCard = HoldemPoker.Cards.Card;
using RFCard = RFIDPoker.Api.Models.Card;

namespace RFIDPoker.Api.Services;

public record EquityResult(double WinPercentage, double TiePercentage, double LosePercentage);

public interface IEquityCalculator
{
    Task<Dictionary<int, EquityResult>> CalculateEquityAsync(
        List<Player> activePlayers,
        List<RFCard> communityCards,
        IEnumerable<RFCard>? deadCards = null,
        int iterations = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Equity calculator backed by the HoldemPoker.Evaluator NuGet package. That library exposes
/// <see cref="HoldemHandEvaluator.GetHandRanking(HoldemCard[])"/>, a lightning-fast 7-card
/// evaluator returning a single int where LOWER = STRONGER. We use exact enumeration on the
/// flop and turn (variance-free) and Monte Carlo preflop (scaled by player count to keep
/// per-seat standard error small in multi-way pots).
/// </summary>
public class EquityCalculator : IEquityCalculator
{
    // Above this many remaining-board combinations we sample instead of enumerating.
    // Turn: <= ~46, Flop: <= ~C(46,2)=1035 -> both always exact. Preflop is far above.
    private const int ExactEnumerationBoardCombosLimit = 2_500;

    public Task<Dictionary<int, EquityResult>> CalculateEquityAsync(
        List<Player> activePlayers,
        List<RFCard> communityCards,
        IEnumerable<RFCard>? deadCards = null,
        int iterations = 0,
        CancellationToken cancellationToken = default)
    {
        // Scale preflop iterations linearly with player count. Per-seat expected equity is
        // ~1/N, and per-seat standard error is ~sqrt(p(1-p)/iters); scaling with N keeps the
        // per-seat SE roughly constant across table sizes.
        if (iterations <= 0)
        {
            var playerScale = Math.Max(2, activePlayers.Count) / 2.0; // 2p=>1x, 9p=>4.5x
            var baseIters = communityCards.Count switch
            {
                0 => 25_000, // preflop
                3 => 8_000,  // flop (usually falls into exact enumeration)
                4 => 3_000,  // turn (always falls into exact enumeration)
                _ => 1_000
            };
            iterations = (int)Math.Ceiling(baseIters * playerScale);
        }

        var dead = deadCards?.ToList() ?? [];
        return Task.Run(() => Calculate(activePlayers, communityCards, dead, iterations, cancellationToken), cancellationToken);
    }

    private static Dictionary<int, EquityResult> Calculate(
        List<Player> activePlayers,
        List<RFCard> communityCards,
        List<RFCard> deadCards,
        int iterations,
        CancellationToken ct)
    {
        var seats = activePlayers.Select(p => p.SeatNumber).ToArray();
        var holeCards = activePlayers
            .Select(p => new[] { ToHoldem(p.HoleCards[0]), ToHoldem(p.HoleCards[1]) })
            .ToArray();
        var boardFixed = communityCards.Select(ToHoldem).ToArray();

        // River: deterministic single evaluation.
        if (boardFixed.Length == 5)
        {
            var wins = new int[seats.Length];
            var ties = new int[seats.Length];
            EvaluateOneBoard(holeCards, boardFixed, wins, ties);
            return BuildResults(seats, wins, ties, 1);
        }

        // Build remaining deck excluding all used cards.
        var usedCards = new HashSet<RFCard>(
            communityCards
                .Concat(activePlayers.SelectMany(p => p.HoleCards))
                .Concat(deadCards));
        var remainingDeck = BuildDeck().Where(c => !usedCards.Contains(c)).Select(ToHoldem).ToArray();
        var cardsNeeded = 5 - boardFixed.Length;

        // Prefer exact enumeration when feasible (turn and flop always qualify).
        var totalCombos = CountCombinations(remainingDeck.Length, cardsNeeded);
        if (totalCombos > 0 && totalCombos <= ExactEnumerationBoardCombosLimit)
        {
            return EnumerateExact(seats, holeCards, boardFixed, remainingDeck, cardsNeeded, ct);
        }

        return MonteCarlo(seats, holeCards, boardFixed, remainingDeck, cardsNeeded, iterations, ct);
    }

    private static Dictionary<int, EquityResult> EnumerateExact(
        int[] seats,
        HoldemCard[][] holeCards,
        HoldemCard[] boardFixed,
        HoldemCard[] remainingDeck,
        int cardsNeeded,
        CancellationToken ct)
    {
        // Parallelise over the outermost combination index. Each thread accumulates locally.
        var globalWins = new long[seats.Length];
        var globalTies = new long[seats.Length];
        long globalRunouts = 0;
        var mergeLock = new object();

        if (cardsNeeded == 1)
        {
            // Turn -> river: at most ~46 runouts. Single-threaded is fine (parallel overhead > work).
            var board = new HoldemCard[5];
            Array.Copy(boardFixed, board, boardFixed.Length);
            var wins = new int[seats.Length];
            var ties = new int[seats.Length];
            long runouts = 0;
            for (int i = 0; i < remainingDeck.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                board[boardFixed.Length] = remainingDeck[i];
                EvaluateOneBoard(holeCards, board, wins, ties);
                runouts++;
            }
            return BuildResults(seats, wins, ties, runouts);
        }

        if (cardsNeeded == 2)
        {
            // Flop -> river: up to C(46,2)=1035 runouts. Parallelised across outer index.
            Parallel.For(
                0, remainingDeck.Length - 1,
                new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                () => (w: new int[seats.Length], t: new int[seats.Length], r: 0L, board: BuildBoard(boardFixed, 2)),
                (i, _, local) =>
                {
                    local.board[boardFixed.Length] = remainingDeck[i];
                    for (int j = i + 1; j < remainingDeck.Length; j++)
                    {
                        local.board[boardFixed.Length + 1] = remainingDeck[j];
                        EvaluateOneBoard(holeCards, local.board, local.w, local.t);
                        local.r++;
                    }
                    return local;
                },
                local =>
                {
                    lock (mergeLock)
                    {
                        for (int i = 0; i < seats.Length; i++)
                        {
                            globalWins[i] += local.w[i];
                            globalTies[i] += local.t[i];
                        }
                        globalRunouts += local.r;
                    }
                });
            return BuildResults(seats, globalWins, globalTies, globalRunouts);
        }

        // Generic fallback (unused for standard poker board sizes but kept for safety).
        var board2 = BuildBoard(boardFixed, cardsNeeded);
        var wins2 = new int[seats.Length];
        var ties2 = new int[seats.Length];
        long runouts2 = 0;
        Enumerate(remainingDeck, cardsNeeded, 0, 0, board2, boardFixed.Length,
            () =>
            {
                ct.ThrowIfCancellationRequested();
                EvaluateOneBoard(holeCards, board2, wins2, ties2);
                runouts2++;
            });
        return BuildResults(seats, wins2, ties2, runouts2);
    }

    private static Dictionary<int, EquityResult> MonteCarlo(
        int[] seats,
        HoldemCard[][] holeCards,
        HoldemCard[] boardFixed,
        HoldemCard[] remainingDeck,
        int cardsNeeded,
        int iterations,
        CancellationToken ct)
    {
        var totalWins = new long[seats.Length];
        var totalTies = new long[seats.Length];
        var mergeLock = new object();

        Parallel.For(
            0, iterations,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            () => new SimState(remainingDeck, boardFixed, seats.Length),
            (i, _, state) =>
            {
                state.DealBoard(cardsNeeded);
                EvaluateOneBoard(holeCards, state.Board, state.Wins, state.Ties);
                return state;
            },
            state =>
            {
                lock (mergeLock)
                {
                    for (int i = 0; i < seats.Length; i++)
                    {
                        totalWins[i] += state.Wins[i];
                        totalTies[i] += state.Ties[i];
                    }
                }
            });

        return BuildResults(seats, totalWins, totalTies, iterations);
    }

    /// <summary>
    /// Evaluates one fully-dealt board across all seats. Winners get a win, ties get a tie
    /// (tracked separately so the API can report both win% and tie% for each seat).
    /// Uses HoldemHandEvaluator.GetHandRanking where LOWER = STRONGER.
    /// </summary>
    private static void EvaluateOneBoard(HoldemCard[][] holeCards, HoldemCard[] board, int[] wins, int[] ties)
    {
        Span<HoldemCard> seven = stackalloc HoldemCard[7];
        for (int i = 0; i < board.Length; i++) seven[2 + i] = board[i];

        int bestRank = int.MaxValue;
        int bestSeat = -1;
        int tieCount = 0;
        Span<int> tied = stackalloc int[holeCards.Length];

        // GetHandRanking accepts an array; copy from Span to a small heap-free wrapper. The
        // library's API takes Card[], so we keep a pooled array per call via a small array.
        var sevenArr = _sevenArrayPool.Value!;
        for (int s = 0; s < holeCards.Length; s++)
        {
            sevenArr[0] = holeCards[s][0];
            sevenArr[1] = holeCards[s][1];
            for (int i = 0; i < board.Length; i++) sevenArr[2 + i] = board[i];

            int rank = HoldemHandEvaluator.GetHandRanking(sevenArr);
            if (rank < bestRank)
            {
                bestRank = rank;
                bestSeat = s;
                tieCount = 0;
            }
            else if (rank == bestRank)
            {
                if (tieCount == 0) tied[tieCount++] = bestSeat;
                tied[tieCount++] = s;
            }
        }

        if (bestSeat < 0) return;
        if (tieCount == 0) wins[bestSeat]++;
        else for (int i = 0; i < tieCount; i++) ties[tied[i]]++;
    }

    // Per-thread reusable 7-card buffer; avoids allocating a Card[7] on every seat evaluation.
    private static readonly ThreadLocal<HoldemCard[]> _sevenArrayPool = new(() => new HoldemCard[7]);

    private static Dictionary<int, EquityResult> BuildResults(int[] seats, int[] wins, int[] ties, long denomLong)
    {
        var denom = Math.Max(1L, denomLong);
        var results = new Dictionary<int, EquityResult>();
        for (int i = 0; i < seats.Length; i++)
        {
            double w = wins[i] * 100.0 / denom;
            double t = ties[i] * 100.0 / denom;
            results[seats[i]] = new EquityResult(w, t, 100 - w - t);
        }
        return results;
    }

    private static Dictionary<int, EquityResult> BuildResults(int[] seats, long[] wins, long[] ties, long denomLong)
    {
        var denom = Math.Max(1L, denomLong);
        var results = new Dictionary<int, EquityResult>();
        for (int i = 0; i < seats.Length; i++)
        {
            double w = wins[i] * 100.0 / denom;
            double t = ties[i] * 100.0 / denom;
            results[seats[i]] = new EquityResult(w, t, 100 - w - t);
        }
        return results;
    }

    private static HoldemCard[] BuildBoard(HoldemCard[] fixedBoard, int extra)
    {
        var b = new HoldemCard[fixedBoard.Length + extra];
        Array.Copy(fixedBoard, b, fixedBoard.Length);
        return b;
    }

    private static void Enumerate(HoldemCard[] deck, int k, int start, int depth,
        HoldemCard[] board, int baseCount, Action onCombo)
    {
        if (depth == k) { onCombo(); return; }
        int end = deck.Length - (k - depth) + 1;
        for (int i = start; i < end; i++)
        {
            board[baseCount + depth] = deck[i];
            Enumerate(deck, k, i + 1, depth + 1, board, baseCount, onCombo);
        }
    }

    private static long CountCombinations(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        if (k > n - k) k = n - k;
        long r = 1;
        for (int i = 1; i <= k; i++)
        {
            r = r * (n - k + i) / i;
            if (r > int.MaxValue) return int.MaxValue;
        }
        return r;
    }

    private static List<RFCard> BuildDeck()
    {
        var deck = new List<RFCard>(52);
        foreach (Suit suit in Enum.GetValues<Suit>())
            foreach (Rank rank in Enum.GetValues<Rank>())
                deck.Add(new RFCard(rank, suit));
        return deck;
    }

    /// <summary>Converts our domain card to the HoldemPoker library's Card type.</summary>
    private static HoldemCard ToHoldem(RFCard c)
    {
        // Rank enum values (Two=2..Ace=14) map to HoldemPoker notation "2","3",...,"T","J","Q","K","A".
        string rank = c.Rank switch
        {
            Rank.Two => "2",
            Rank.Three => "3",
            Rank.Four => "4",
            Rank.Five => "5",
            Rank.Six => "6",
            Rank.Seven => "7",
            Rank.Eight => "8",
            Rank.Nine => "9",
            Rank.Ten => "T",
            Rank.Jack => "J",
            Rank.Queen => "Q",
            Rank.King => "K",
            Rank.Ace => "A",
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };
        string suit = c.Suit switch
        {
            Suit.Hearts => "h",
            Suit.Diamonds => "d",
            Suit.Clubs => "c",
            Suit.Spades => "s",
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };
        return HoldemCard.Parse(rank + suit);
    }

    private sealed class SimState
    {
        private readonly HoldemCard[] _deck;
        private readonly HoldemCard[] _boardFixed;
        public readonly HoldemCard[] Board;
        public readonly int[] Wins;
        public readonly int[] Ties;
        private readonly Random _rng;

        public SimState(HoldemCard[] deckTemplate, HoldemCard[] boardFixed, int playerCount)
        {
            _deck = (HoldemCard[])deckTemplate.Clone();
            _boardFixed = boardFixed;
            Board = new HoldemCard[5];
            Array.Copy(boardFixed, Board, boardFixed.Length);
            Wins = new int[playerCount];
            Ties = new int[playerCount];
            _rng = new Random(Guid.NewGuid().GetHashCode());
        }

        /// <summary>Partial Fisher-Yates: shuffles the first N slots then copies them to Board.</summary>
        public void DealBoard(int cardsNeeded)
        {
            for (int i = 0; i < cardsNeeded; i++)
            {
                int j = _rng.Next(i, _deck.Length);
                (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
                Board[_boardFixed.Length + i] = _deck[i];
            }
        }
    }
}


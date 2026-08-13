using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface IHandEvaluator
{
    HandResult? EvaluateBestHand(List<Card> holeCards, List<Card> communityCards);
}

public class HandEvaluator : IHandEvaluator
{
    public HandResult? EvaluateBestHand(List<Card> holeCards, List<Card> communityCards)
    {
        if (holeCards.Count < 2)
            return null;

        var allCards = holeCards.Concat(communityCards).ToList();
        if (allCards.Count < 5)
            return null;

        var combinations = GetCombinations(allCards, 5);
        HandResult? best = null;

        foreach (var combo in combinations)
        {
            var result = EvaluateFiveCards(combo);
            if (best is null || CompareHands(result, best) > 0)
                best = result;
        }

        return best;
    }

    private static HandResult EvaluateFiveCards(List<Card> cards)
    {
        var sorted = cards.OrderByDescending(c => c.Rank).ToList();
        var isFlush = cards.All(c => c.Suit == cards[0].Suit);
        var isStraight = IsStraight(sorted, out var straightHigh);

        var groups = sorted.GroupBy(c => c.Rank)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .ToList();

        var counts = groups.Select(g => g.Count()).ToList();

        // Order the "best five" so it can be compared element-by-element against another
        // hand of the same rank: matched cards (pair/trips/quads) first, then kickers
        // (also grouped) in descending order.
        var structured = groups.SelectMany(g => g).ToList();

        if (isFlush && isStraight)
        {
            // For a wheel (A-2-3-4-5) the effective ordering puts the 5 first (Ace plays low).
            var sfCards = straightHigh == Rank.Five
                ? sorted.Where(c => c.Rank != Rank.Ace).Concat(sorted.Where(c => c.Rank == Rank.Ace)).ToList()
                : sorted;

            if (straightHigh == Rank.Ace)
                return new HandResult(Models.HandRank.RoyalFlush, "Royal Flush", sfCards, []);

            return new HandResult(Models.HandRank.StraightFlush,
                $"{straightHigh} High Straight Flush", sfCards, []);
        }

        if (counts is [4, 1])
        {
            var quadRank = groups[0].Key;
            var kicker = groups[1].First();
            return new HandResult(Models.HandRank.FourOfAKind,
                $"Four of a Kind, {quadRank}s", structured, [kicker]);
        }

        if (counts is [3, 2])
        {
            return new HandResult(Models.HandRank.FullHouse,
                $"Full House, {groups[0].Key}s over {groups[1].Key}s", structured, []);
        }

        if (isFlush)
        {
            return new HandResult(Models.HandRank.Flush,
                $"{sorted[0].Rank} High Flush", sorted, []);
        }

        if (isStraight)
        {
            var stCards = straightHigh == Rank.Five
                ? sorted.Where(c => c.Rank != Rank.Ace).Concat(sorted.Where(c => c.Rank == Rank.Ace)).ToList()
                : sorted;
            return new HandResult(Models.HandRank.Straight,
                $"{straightHigh} High Straight", stCards, []);
        }

        if (counts is [3, 1, 1])
        {
            var kickers = groups.Skip(1).Select(g => g.First()).ToList();
            return new HandResult(Models.HandRank.ThreeOfAKind,
                $"Three of a Kind, {groups[0].Key}s", structured, kickers);
        }

        if (counts is [2, 2, 1])
        {
            var kicker = groups[2].First();
            return new HandResult(Models.HandRank.TwoPair,
                $"Two Pair, {groups[0].Key}s and {groups[1].Key}s", structured, [kicker]);
        }

        if (counts is [2, 1, 1, 1])
        {
            var kickers = groups.Skip(1).Select(g => g.First()).ToList();
            return new HandResult(Models.HandRank.OnePair,
                $"Pair of {groups[0].Key}s", structured, kickers);
        }

        var highKickers = sorted.Skip(1).ToList();
        return new HandResult(Models.HandRank.HighCard,
            $"{sorted[0].Rank} High", sorted, highKickers);
    }

    private static bool IsStraight(List<Card> sorted, out Rank highCard)
    {
        var ranks = sorted.Select(c => (int)c.Rank).Distinct().OrderByDescending(r => r).ToList();
        highCard = (Rank)ranks[0];

        if (ranks.Count == 5 && ranks[0] - ranks[4] == 4)
            return true;

        // Ace-low straight (A-2-3-4-5)
        if (ranks.Contains((int)Rank.Ace) && ranks.Contains((int)Rank.Two) &&
            ranks.Contains((int)Rank.Three) && ranks.Contains((int)Rank.Four) &&
            ranks.Contains((int)Rank.Five))
        {
            highCard = Rank.Five;
            return true;
        }

        return false;
    }

    public static int CompareHands(HandResult a, HandResult b)
    {
        if (a.Rank != b.Rank)
            return a.Rank.CompareTo(b.Rank);

        // Compare card by card
        for (int i = 0; i < Math.Min(a.BestFiveCards.Count, b.BestFiveCards.Count); i++)
        {
            var cmp = a.BestFiveCards[i].Rank.CompareTo(b.BestFiveCards[i].Rank);
            if (cmp != 0) return cmp;
        }

        return 0;
    }

    private static IEnumerable<List<Card>> GetCombinations(List<Card> cards, int k)
    {
        if (k == 0)
        {
            yield return [];
            yield break;
        }

        for (int i = 0; i <= cards.Count - k; i++)
        {
            foreach (var combo in GetCombinations(cards[(i + 1)..], k - 1))
            {
                combo.Insert(0, cards[i]);
                yield return combo;
            }
        }
    }
}

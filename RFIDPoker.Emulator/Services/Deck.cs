using RFIDPoker.Emulator.Options;
using RFIDPoker.Emulator.Services;

namespace RFIDPoker.Emulator.Services;

/// <summary>
/// One card in the emulated deck. Values mirror the API's <c>Rank</c>/<c>Suit</c> enums
/// so we can POST them straight to the calibration endpoint.
/// </summary>
public sealed record EmuCard(int Rank, int Suit)
{
    /// <summary>Rank glyph for tag ids: 2..9,T,J,Q,K,A.</summary>
    public string RankChar => Rank switch
    {
        10 => "T",
        11 => "J",
        12 => "Q",
        13 => "K",
        14 => "A",
        _  => Rank.ToString()
    };

    /// <summary>Suit glyph for tag ids: H,D,C,S.</summary>
    public string SuitChar => Suit switch
    {
        0 => "H",
        1 => "D",
        2 => "C",
        3 => "S",
        _ => "?"
    };

    public string TagId(string prefix) => $"{prefix}{RankChar}{SuitChar}";
}

public static class Deck
{
    public static IReadOnlyList<EmuCard> Standard { get; } = BuildStandard();

    private static List<EmuCard> BuildStandard()
    {
        var cards = new List<EmuCard>(52);
        for (var suit = 0; suit < 4; suit++)
            for (var rank = 2; rank <= 14; rank++)
                cards.Add(new EmuCard(rank, suit));
        return cards;
    }

    public static List<EmuCard> Shuffled(Random rng)
    {
        var list = Standard.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}

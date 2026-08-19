namespace RFIDPoker.Api.Models;

public class Player
{
    public int SeatNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Card> HoleCards { get; set; } = [];
    /// <summary>
    /// Every hole card this seat has been dealt during the current hand. Persists across
    /// tag flicker / muck transitions so a "fold via muck" can still be attributed to the
    /// original seat even after the physical cards have left it.
    /// </summary>
    public HashSet<Card> DealtThisHand { get; set; } = [];
    public bool IsFolded { get; set; }

    /// <summary>
    /// When non-null, the seat currently has no live tags but still holds preserved hole
    /// cards pending an auto-fold decision. Cleared when cards reappear or on fold/new hand.
    /// </summary>
    public DateTimeOffset? CardsMissingSince { get; set; }

    /// <summary>
    /// Sticky snapshot of both hole cards once the seat has held them continuously for
    /// the configured latch-on window. While set, brief partial reads (1 card, or 0 cards)
    /// don't disturb the displayed hole cards; the latch is released on muck/fold or after
    /// the configured absence window (handled by <see cref="MissingCardsAutoFoldService"/>).
    /// </summary>
    public List<Card>? LatchedHoleCards { get; set; }

    /// <summary>
    /// When the seat first started showing a stable 2-card pair matching the current
    /// (or pending) latch candidate. Used to gate the 2-second latch-on window.
    /// </summary>
    public DateTimeOffset? HoleCardsStableSince { get; set; }

    /// <summary>Optional chip count. Null means "not tracked" and won't be shown on the UI.</summary>
    public long? ChipCount { get; set; }
}

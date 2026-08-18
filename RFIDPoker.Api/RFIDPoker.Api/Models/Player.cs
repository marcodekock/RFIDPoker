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

    /// <summary>Optional chip count. Null means "not tracked" and won't be shown on the UI.</summary>
    public long? ChipCount { get; set; }
}

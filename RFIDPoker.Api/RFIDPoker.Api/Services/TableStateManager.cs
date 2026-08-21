using Microsoft.Extensions.Options;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface ITableStateManager
{
    List<Player> Players { get; }
    List<Card> CommunityCards { get; }
    IReadOnlyList<Card> MuckedCards { get; }
    Street CurrentStreet { get; }

    /// <summary>Blinds display string, e.g. "2000/4000". Null/empty means no blinds set.</summary>
    string? Blinds { get; }
    void SetBlinds(string? blinds);

    /// <summary>
    /// The deck currently "locked in" for this hand. Set the first time a player hole
    /// card is accepted; every subsequent tag read is filtered to this deck until
    /// <see cref="NewHand"/> clears the table. This lets the dealer shuffle one deck
    /// while another is in play without cross-contamination. Null means no deck is
    /// locked yet (any enabled deck's tags may be read).
    /// </summary>
    int? ActiveDeckId { get; }

    /// <summary>
    /// Attempts to lock the table to <paramref name="deckId"/>. If no deck is currently
    /// locked the lock is set and true is returned. If a different deck is already
    /// locked, returns false and the lock is unchanged.
    /// </summary>
    bool TryLockDeck(int deckId);

    event Action? StateChanged;

    /// <summary>
    /// Fired whenever <see cref="NewHand"/> resets the table. Consumers (e.g. the RFID
    /// reader's community-card latch) should treat this as an authoritative "wipe" signal
    /// so stale latch state can't resurrect the previous hand on the next eviction tick.
    /// </summary>
    event Action? HandReset;

    void SetPlayerHoleCards(int seatNumber, List<Card> cards);
    void SetPlayerName(int seatNumber, string name);
    IReadOnlyDictionary<int, string> SetPlayerNames(IReadOnlyDictionary<int, string> namesBySeat);
    void SetPlayerChipCount(int seatNumber, long? chipCount);
    void AddMuckedCards(IEnumerable<Card> cards);
    void RemoveMuckedCards(IEnumerable<Card> cards);
    void FoldPlayer(int seatNumber);
    void UnfoldPlayer(int seatNumber);
    void SetCommunityCards(List<Card> cards);
    void NewHand();
    List<Player> GetActivePlayers();
    List<Player> GetFoldedPlayers();
}

public class TableStateManager : ITableStateManager
{
    private readonly Lock _lock = new();
    private readonly TimeSpan _latchOn;

    public TableStateManager(IOptions<RfidConfig> rfidOptions)
    {
        _latchOn = TimeSpan.FromMilliseconds(Math.Max(0, rfidOptions.Value.CardLatchOnMs));
    }

    public List<Player> Players { get; } = [];
    public List<Card> CommunityCards { get; } = [];
    private readonly List<Card> _muckedCards = [];
    private readonly HashSet<Card> _muckedSet = [];

    private string? _blinds;
    private int? _activeDeckId;
    public string? Blinds
    {
        get { lock (_lock) { return _blinds; } }
    }

    public int? ActiveDeckId
    {
        get { lock (_lock) { return _activeDeckId; } }
    }

    public bool TryLockDeck(int deckId)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_activeDeckId is null)
            {
                _activeDeckId = deckId;
                changed = true;
            }
            else if (_activeDeckId.Value != deckId)
            {
                return false;
            }
        }
        if (changed) StateChanged?.Invoke();
        return true;
    }

    public void SetBlinds(string? blinds)
    {
        var normalized = string.IsNullOrWhiteSpace(blinds) ? null : blinds.Trim();
        lock (_lock)
        {
            if (_blinds == normalized) return;
            _blinds = normalized;
        }
        StateChanged?.Invoke();
    }

    public IReadOnlyList<Card> MuckedCards
    {
        get { lock (_lock) { return _muckedCards.ToList(); } }
    }

    public void AddMuckedCards(IEnumerable<Card> cards)
    {
        bool added = false;
        lock (_lock)
        {
            foreach (var c in cards)
            {
                if (_muckedSet.Add(c))
                {
                    _muckedCards.Add(c);
                    added = true;
                }
            }
        }
        if (added) StateChanged?.Invoke();
    }

    public void RemoveMuckedCards(IEnumerable<Card> cards)
    {
        bool removed = false;
        lock (_lock)
        {
            foreach (var c in cards)
            {
                if (_muckedSet.Remove(c))
                {
                    _muckedCards.Remove(c);
                    removed = true;
                }
            }
        }
        if (removed) StateChanged?.Invoke();
    }
    public Street CurrentStreet => CommunityCards.Count switch
    {
        0 => Street.PreFlop,
        3 => Street.Flop,
        4 => Street.Turn,
        >= 5 => Street.River,
        _ => Street.PreFlop
    };

    public event Action? StateChanged;
    public event Action? HandReset;

    public void SetPlayerHoleCards(int seatNumber, List<Card> cards)
    {
        lock (_lock)
        {
            var player = GetOrCreatePlayer(seatNumber);
            // Once a player is folded, keep the hole cards they were dealt so they
            // still show up in the folded list even after the physical cards leave
            // the seat antenna (e.g. slid into the muck).
            if (player.IsFolded && cards.Count == 0) return;

            // Latch semantics: once both hole cards have been continuously present for
            // the configured latch-on window, snapshot them so a brief 1-card / 0-card
            // reader drop doesn't wipe the HUD or churn equity. The latch is released
            // by fold/muck (FoldPlayer / NewHand) or by the missing-cards auto-fold
            // after CardLatchOffMs (== MissingCardsFoldMs) of continuous absence.
            var now = DateTimeOffset.UtcNow;

            if (cards.Count == 2)
            {
                var matchesLatch = player.LatchedHoleCards is { Count: 2 } latched
                    && cards.All(latched.Contains);

                if (player.LatchedHoleCards is null || !matchesLatch)
                {
                    // Same 2-card set holding steady? Start / continue the latch-on timer.
                    var samePending = player.HoleCards.Count == 2
                        && cards.All(player.HoleCards.Contains);
                    if (samePending)
                    {
                        player.HoleCardsStableSince ??= now;
                        if (player.LatchedHoleCards is null
                            && now - player.HoleCardsStableSince.Value >= _latchOn)
                        {
                            player.LatchedHoleCards = cards.ToList();
                        }
                        else if (player.LatchedHoleCards is not null && !matchesLatch)
                        {
                            // Different pair now stable — swap the latch.
                            player.LatchedHoleCards = cards.ToList();
                        }
                    }
                    else
                    {
                        player.HoleCardsStableSince = now;
                        if (player.LatchedHoleCards is not null && !matchesLatch)
                        {
                            // Genuinely different pair on the seat — treat as new hand for this seat.
                            player.LatchedHoleCards = null;
                        }
                    }
                }

                player.CardsMissingSince = null;
                player.HoleCards = cards;
                foreach (var c in cards) player.DealtThisHand.Add(c);
            }
            else if (cards.Count == 1)
            {
                // Partial read. If we have a latched pair that includes this card,
                // treat as a flicker and keep the latched pair on display.
                if (player.LatchedHoleCards is { Count: 2 } latched
                    && latched.Contains(cards[0]))
                {
                    player.CardsMissingSince = null;
                    player.HoleCards = latched.ToList();
                    foreach (var c in cards) player.DealtThisHand.Add(c);
                }
                else
                {
                    // No latch (or the single card doesn't belong to it): fall back
                    // to legacy behavior — reset stability timer and reflect raw state.
                    player.HoleCardsStableSince = null;
                    player.CardsMissingSince = null;
                    player.HoleCards = cards;
                    foreach (var c in cards) player.DealtThisHand.Add(c);
                }
            }
            else // cards.Count == 0
            {
                // Cards physically left the seat. If latched, keep displaying the latched
                // pair; the auto-fold service will release the latch after CardLatchOffMs.
                if (player.LatchedHoleCards is { Count: 2 } latched)
                {
                    player.CardsMissingSince ??= now;
                    player.HoleCards = latched.ToList();
                    return;
                }
                if (player.HoleCards.Count > 0 || player.DealtThisHand.Count > 0)
                {
                    // Un-latched flicker path: preserve last known, arm the grace timer.
                    player.CardsMissingSince ??= now;
                    return;
                }
                // Never dealt in this hand - nothing to preserve.
                player.HoleCardsStableSince = null;
                player.HoleCards = cards;
                return;
            }
        }
        StateChanged?.Invoke();
    }

    public void FoldPlayer(int seatNumber)
    {
        lock (_lock)
        {
            var player = Players.FirstOrDefault(p => p.SeatNumber == seatNumber);
            if (player is not null)
            {
                player.IsFolded = true;
                player.CardsMissingSince = null;
                player.LatchedHoleCards = null;
                player.HoleCardsStableSince = null;
            }
        }
        StateChanged?.Invoke();
    }

    public void UnfoldPlayer(int seatNumber)
    {
        lock (_lock)
        {
            var player = Players.FirstOrDefault(p => p.SeatNumber == seatNumber);
            if (player is not null)
            {
                player.IsFolded = false;
                player.CardsMissingSince = null;
            }
        }
        StateChanged?.Invoke();
    }

    public void SetPlayerName(int seatNumber, string name)
    {
        lock (_lock)
        {
            var player = GetOrCreatePlayer(seatNumber);
            player.Name = string.IsNullOrWhiteSpace(name) ? $"Player {seatNumber}" : name.Trim();
        }
        StateChanged?.Invoke();
    }

    public IReadOnlyDictionary<int, string> SetPlayerNames(IReadOnlyDictionary<int, string> namesBySeat)
    {
        var applied = new Dictionary<int, string>();
        lock (_lock)
        {
            foreach (var (seat, name) in namesBySeat)
            {
                var player = GetOrCreatePlayer(seat);
                player.Name = string.IsNullOrWhiteSpace(name) ? $"Player {seat}" : name.Trim();
                applied[seat] = player.Name;
            }
        }
        if (applied.Count > 0) StateChanged?.Invoke();
        return applied;
    }

    public void SetPlayerChipCount(int seatNumber, long? chipCount)
    {
        lock (_lock)
        {
            var player = GetOrCreatePlayer(seatNumber);
            player.ChipCount = chipCount is null or < 0 ? null : chipCount;
        }
        StateChanged?.Invoke();
    }

    public void SetCommunityCards(List<Card> cards)
    {
        lock (_lock)
        {
            CommunityCards.Clear();
            CommunityCards.AddRange(cards);
        }
        StateChanged?.Invoke();
    }

    public void NewHand()
    {
        lock (_lock)
        {
            CommunityCards.Clear();
            _muckedCards.Clear();
            _muckedSet.Clear();
            _activeDeckId = null;
            foreach (var p in Players)
            {
                p.HoleCards.Clear();
                p.DealtThisHand.Clear();
                p.IsFolded = false;
                p.CardsMissingSince = null;
                p.LatchedHoleCards = null;
                p.HoleCardsStableSince = null;
            }
        }
        HandReset?.Invoke();
        StateChanged?.Invoke();
    }

    public List<Player> GetActivePlayers()
    {
        lock (_lock) { return Players.Where(p => !p.IsFolded).ToList(); }
    }

    public List<Player> GetFoldedPlayers()
    {
        lock (_lock) { return Players.Where(p => p.IsFolded).ToList(); }
    }

    private Player GetOrCreatePlayer(int seatNumber)
    {
        var player = Players.FirstOrDefault(p => p.SeatNumber == seatNumber);
        if (player is null)
        {
            player = new Player { SeatNumber = seatNumber, Name = $"Player {seatNumber}" };
            Players.Add(player);
        }
        return player;
    }
}

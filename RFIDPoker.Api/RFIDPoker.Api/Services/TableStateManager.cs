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

    event Action? StateChanged;

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

    public List<Player> Players { get; } = [];
    public List<Card> CommunityCards { get; } = [];
    private readonly List<Card> _muckedCards = [];
    private readonly HashSet<Card> _muckedSet = [];

    private string? _blinds;
    public string? Blinds
    {
        get { lock (_lock) { return _blinds; } }
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

    public void SetPlayerHoleCards(int seatNumber, List<Card> cards)
    {
        lock (_lock)
        {
            var player = GetOrCreatePlayer(seatNumber);
            // Once a player is folded, keep the hole cards they were dealt so they
            // still show up in the folded list even after the physical cards leave
            // the seat antenna (e.g. slid into the muck).
            if (player.IsFolded && cards.Count == 0) return;
            player.HoleCards = cards;
            foreach (var c in cards) player.DealtThisHand.Add(c);
        }
        StateChanged?.Invoke();
    }

    public void FoldPlayer(int seatNumber)
    {
        lock (_lock)
        {
            var player = Players.FirstOrDefault(p => p.SeatNumber == seatNumber);
            if (player is not null) player.IsFolded = true;
        }
        StateChanged?.Invoke();
    }

    public void UnfoldPlayer(int seatNumber)
    {
        lock (_lock)
        {
            var player = Players.FirstOrDefault(p => p.SeatNumber == seatNumber);
            if (player is not null) player.IsFolded = false;
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
            foreach (var p in Players)
            {
                p.HoleCards.Clear();
                p.DealtThisHand.Clear();
                p.IsFolded = false;
            }
        }
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

using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface ITableStateManager
{
    List<Player> Players { get; }
    List<Card> CommunityCards { get; }
    Street CurrentStreet { get; }
    event Action? StateChanged;

    void SetPlayerHoleCards(int seatNumber, List<Card> cards);
    void FoldPlayer(int seatNumber);
    void UnfoldPlayer(int seatNumber);
    void SetCommunityCards(List<Card> cards);
    void AddCommunityCard(Card card);
    void RemoveCommunityCard(Card card);
    void SetDealer(int seatNumber);
    void NewHand();
    void AddPlayer(int seatNumber, string name);
    void RemovePlayer(int seatNumber);
    List<Player> GetActivePlayers();
    List<Player> GetFoldedPlayers();
}

public class TableStateManager : ITableStateManager
{
    private readonly Lock _lock = new();

    public List<Player> Players { get; } = [];
    public List<Card> CommunityCards { get; } = [];
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
            player.HoleCards = cards;
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

    public void SetCommunityCards(List<Card> cards)
    {
        lock (_lock)
        {
            CommunityCards.Clear();
            CommunityCards.AddRange(cards);
        }
        StateChanged?.Invoke();
    }

    public void AddCommunityCard(Card card)
    {
        lock (_lock) { CommunityCards.Add(card); }
        StateChanged?.Invoke();
    }

    public void RemoveCommunityCard(Card card)
    {
        lock (_lock) { CommunityCards.Remove(card); }
        StateChanged?.Invoke();
    }

    public void SetDealer(int seatNumber)
    {
        lock (_lock)
        {
            foreach (var p in Players) p.IsDealer = p.SeatNumber == seatNumber;
        }
        StateChanged?.Invoke();
    }

    public void NewHand()
    {
        lock (_lock)
        {
            CommunityCards.Clear();
            foreach (var p in Players)
            {
                p.HoleCards.Clear();
                p.IsFolded = false;
            }
        }
        StateChanged?.Invoke();
    }

    public void AddPlayer(int seatNumber, string name)
    {
        lock (_lock)
        {
            if (Players.All(p => p.SeatNumber != seatNumber))
                Players.Add(new Player { SeatNumber = seatNumber, Name = name });
        }
        StateChanged?.Invoke();
    }

    public void RemovePlayer(int seatNumber)
    {
        lock (_lock)
        {
            Players.RemoveAll(p => p.SeatNumber == seatNumber);
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

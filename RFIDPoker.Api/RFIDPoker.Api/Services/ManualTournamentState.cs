namespace RFIDPoker.Api.Services;

/// <summary>
/// Operator-entered tournament info shown on the overlay when Tournament Director
/// integration is disabled. Persisted via <see cref="ISettingsStore"/>.
/// </summary>
public class ManualTournamentInfo
{
    public int Level { get; set; }
    public int PlayersLeft { get; set; }
    public long TotalChips { get; set; }
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int NextSmallBlind { get; set; }
    public int NextBigBlind { get; set; }

    public bool HasAny =>
        Level > 0 || PlayersLeft > 0 || TotalChips > 0 ||
        SmallBlind > 0 || BigBlind > 0 || NextSmallBlind > 0 || NextBigBlind > 0;
}

public interface IManualTournamentState
{
    ManualTournamentInfo Current { get; }
    event Action? Changed;
    void Set(ManualTournamentInfo value);
}

public class ManualTournamentState : IManualTournamentState
{
    private readonly Lock _lock = new();
    private ManualTournamentInfo _current = new();

    public ManualTournamentInfo Current
    {
        get { lock (_lock) return _current; }
    }

    public event Action? Changed;

    public void Set(ManualTournamentInfo value)
    {
        lock (_lock) _current = value ?? new ManualTournamentInfo();
        Changed?.Invoke();
    }
}

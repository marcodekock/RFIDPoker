namespace RFIDPoker.Api.Services;

/// <summary>
/// Payload posted by Tournament Director to <c>POST /api/tournament-director/webhook</c>.
/// All integer fields — TD sends 0/1 for the boolean-ish ones.
/// </summary>
public class TournamentDirectorUpdate
{
    public int Level { get; set; }
    public int BreakNum { get; set; }

    public int IsRound { get; set; }
    public int IsBreak { get; set; }
    public int NextIsBreak { get; set; }

    public int LevelDuration { get; set; }
    public int NextLevelDuration { get; set; }

    public int Buyins { get; set; }
    public int PlayersLeft { get; set; }

    public int SecondsLeft { get; set; }
    public int ClockPaused { get; set; }
    public int ClockPausedSeconds { get; set; }

    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }

    public int NextSmallBlind { get; set; }
    public int NextBigBlind { get; set; }

    public int ChipCount { get; set; }
}

/// <summary>
/// Latest TD snapshot cached in memory. Consumed by <see cref="PokerAnalysisEngine"/>
/// to override manually-managed blinds, break state, and player count when TD is enabled.
/// </summary>
public interface ITournamentDirectorState
{
    bool IsEnabled { get; }
    TournamentDirectorUpdate? Latest { get; }
    DateTimeOffset? LastUpdatedUtc { get; }

    event Action? Changed;

    void SetEnabled(bool enabled);
    void Apply(TournamentDirectorUpdate update);
}

public class TournamentDirectorState : ITournamentDirectorState
{
    private readonly Lock _lock = new();
    private volatile bool _enabled;
    private TournamentDirectorUpdate? _latest;
    private DateTimeOffset? _lastUpdatedUtc;

    public bool IsEnabled => _enabled;

    public TournamentDirectorUpdate? Latest
    {
        get { lock (_lock) return _latest; }
    }

    public DateTimeOffset? LastUpdatedUtc
    {
        get { lock (_lock) return _lastUpdatedUtc; }
    }

    public event Action? Changed;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (!enabled)
        {
            lock (_lock)
            {
                _latest = null;
                _lastUpdatedUtc = null;
            }
        }
        Changed?.Invoke();
    }

    public void Apply(TournamentDirectorUpdate update)
    {
        lock (_lock)
        {
            _latest = update;
            _lastUpdatedUtc = DateTimeOffset.UtcNow;
        }
        Changed?.Invoke();
    }
}

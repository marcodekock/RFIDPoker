using RFIDPoker.Api.Dtos;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Holds tournament-level state that lives above a single hand: the break timer and
/// (in future) any other session-wide settings. Kept separate from
/// <see cref="ITableStateManager"/> so a break can freeze card evaluation without
/// mutating the physical table state.
/// </summary>
public interface ITournamentStateManager
{
    /// <summary>Raised whenever break state changes (start/pause/resume/stop/tick-to-zero).</summary>
    event Action? StateChanged;

    /// <summary>Current break snapshot; null means no break has ever been configured.</summary>
    BreakStateDto? GetBreakSnapshot();

    /// <summary>True when a break is running or paused. Overlay/display use this to hide cards.</summary>
    bool IsOnBreak { get; }

    /// <summary>Starts (or restarts) a break for the given duration.</summary>
    BreakStateDto StartBreak(int totalSeconds, string? label);

    /// <summary>Pauses the running break. No-op if already paused or no break running.</summary>
    BreakStateDto? PauseBreak();

    /// <summary>Resumes a paused break. No-op if not paused.</summary>
    BreakStateDto? ResumeBreak();

    /// <summary>Adds (or subtracts if negative) seconds to the current break.</summary>
    BreakStateDto? AdjustBreak(int deltaSeconds);

    /// <summary>Ends the break immediately.</summary>
    void StopBreak();

    /// <summary>Fires StateChanged if a break is active; used by the tick service.</summary>
    void Tick();
}

public sealed class TournamentStateManager : ITournamentStateManager
{
    private readonly Lock _lock = new();

    private int _totalSeconds;
    private string? _label;
    private DateTimeOffset? _startedUtc;
    private DateTimeOffset? _pausedUtc;
    private TimeSpan _elapsedBeforePause = TimeSpan.Zero;
    private bool _active;

    public event Action? StateChanged;

    public bool IsOnBreak
    {
        get { lock (_lock) { return _active; } }
    }

    public BreakStateDto? GetBreakSnapshot()
    {
        lock (_lock) { return BuildSnapshotLocked(); }
    }

    public BreakStateDto StartBreak(int totalSeconds, string? label)
    {
        if (totalSeconds < 1) totalSeconds = 1;
        BreakStateDto snap;
        lock (_lock)
        {
            _totalSeconds = totalSeconds;
            _label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            _startedUtc = DateTimeOffset.UtcNow;
            _pausedUtc = null;
            _elapsedBeforePause = TimeSpan.Zero;
            _active = true;
            snap = BuildSnapshotLocked()!;
        }
        StateChanged?.Invoke();
        return snap;
    }

    public BreakStateDto? PauseBreak()
    {
        BreakStateDto? snap;
        lock (_lock)
        {
            if (!_active || _pausedUtc is not null || _startedUtc is null) return BuildSnapshotLocked();
            _pausedUtc = DateTimeOffset.UtcNow;
            _elapsedBeforePause += _pausedUtc.Value - _startedUtc.Value;
            _startedUtc = null;
            snap = BuildSnapshotLocked();
        }
        StateChanged?.Invoke();
        return snap;
    }

    public BreakStateDto? ResumeBreak()
    {
        BreakStateDto? snap;
        lock (_lock)
        {
            if (!_active || _pausedUtc is null) return BuildSnapshotLocked();
            _startedUtc = DateTimeOffset.UtcNow;
            _pausedUtc = null;
            snap = BuildSnapshotLocked();
        }
        StateChanged?.Invoke();
        return snap;
    }

    public BreakStateDto? AdjustBreak(int deltaSeconds)
    {
        BreakStateDto? snap;
        lock (_lock)
        {
            if (!_active) return null;
            _totalSeconds = Math.Max(1, _totalSeconds + deltaSeconds);
            snap = BuildSnapshotLocked();
        }
        StateChanged?.Invoke();
        return snap;
    }

    public void StopBreak()
    {
        lock (_lock)
        {
            if (!_active) return;
            _active = false;
            _startedUtc = null;
            _pausedUtc = null;
            _elapsedBeforePause = TimeSpan.Zero;
            _totalSeconds = 0;
            _label = null;
        }
        StateChanged?.Invoke();
    }

    public void Tick()
    {
        bool fire;
        lock (_lock) { fire = _active; }
        if (fire) StateChanged?.Invoke();
    }

    private BreakStateDto? BuildSnapshotLocked()
    {
        if (!_active) return null;

        var elapsed = _elapsedBeforePause;
        if (_startedUtc is not null && _pausedUtc is null)
            elapsed += DateTimeOffset.UtcNow - _startedUtc.Value;

        var total = TimeSpan.FromSeconds(_totalSeconds);
        var remaining = total - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        return new BreakStateDto
        {
            IsActive = true,
            IsPaused = _pausedUtc is not null,
            Label = _label,
            TotalSeconds = _totalSeconds,
            RemainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds),
            ServerNowUtc = DateTimeOffset.UtcNow
        };
    }
}

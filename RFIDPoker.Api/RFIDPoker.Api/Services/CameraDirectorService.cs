using Microsoft.Extensions.Options;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Live status snapshot exposed to the admin UI.
/// </summary>
public record CameraDirectorStatus(
    bool Enabled,
    bool Connected,
    string? CurrentScene,
    string? DesiredScene,
    bool HandInProgress);

public interface ICameraDirector
{
    CameraDirectorStatus GetStatus();
}

/// <summary>
/// Watches the table state and drives OBS scene switches.
/// - Main scene is selected while a hand is live (at least one non-folded player has
///   2 hole cards AND every non-folded player with any dealt cards has 2 hole cards).
/// - Otherwise the enabled secondary cameras are rotated on a fixed interval.
/// </summary>
public class CameraDirectorService(
    ITableStateManager tableState,
    IObsClient obs,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ObsSettings> settingsMonitor,
    ILogger<CameraDirectorService> logger) : BackgroundService, ICameraDirector
{
    private readonly SemaphoreSlim _tick = new(0, int.MaxValue);
    private List<Camera> _cameras = [];
    private int _secondaryIndex;
    private string? _lastAppliedScene;
    private DateTimeOffset _lastSwitchAt = DateTimeOffset.MinValue;
    private bool _lastHandInProgress;
    private string? _lastDesiredScene;

    public CameraDirectorStatus GetStatus() => new(
        Enabled: settingsMonitor.CurrentValue.Enabled,
        Connected: obs.IsConnected,
        CurrentScene: obs.CurrentScene,
        DesiredScene: _lastDesiredScene,
        HandInProgress: _lastHandInProgress);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadCamerasAsync(stoppingToken);

        void OnStateChanged() => _tick.Release();
        void OnCamerasChanged() { _ = LoadCamerasAsync(stoppingToken); _tick.Release(); }

        tableState.StateChanged += OnStateChanged;
        tableState.HandReset += OnStateChanged;

        // Subscribe once via a scoped repo so the static Changed event carries the callback.
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICameraRepository>();
            repo.Changed += OnCamerasChanged;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var rotate = TimeSpan.FromSeconds(Math.Max(1, settingsMonitor.CurrentValue.SecondaryRotationSeconds));
                using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var timeout = Task.Delay(rotate, tickCts.Token);
                var signal = _tick.WaitAsync(tickCts.Token);
                await Task.WhenAny(timeout, signal);
                tickCts.Cancel();

                try
                {
                    await ApplyAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Camera director tick failed.");
                }
            }
        }
        finally
        {
            tableState.StateChanged -= OnStateChanged;
            tableState.HandReset -= OnStateChanged;
        }
    }

    private async Task LoadCamerasAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICameraRepository>();
            _cameras = await repo.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load cameras.");
        }
    }

    private async Task ApplyAsync(CancellationToken ct)
    {
        var settings = settingsMonitor.CurrentValue;
        if (!settings.Enabled) return;

        var handInProgress = IsHandInProgress();
        _lastHandInProgress = handInProgress;

        Camera? target;
        if (handInProgress)
        {
            target = _cameras.FirstOrDefault(c => c.Enabled && c.Role == CameraRole.Main);
        }
        else
        {
            var secondaries = _cameras
                .Where(c => c.Enabled && c.Role == CameraRole.Secondary)
                .ToList();
            if (secondaries.Count == 0)
            {
                // Fall back to main if no secondary is configured.
                target = _cameras.FirstOrDefault(c => c.Enabled && c.Role == CameraRole.Main);
            }
            else
            {
                _secondaryIndex = (_secondaryIndex + 1) % secondaries.Count;
                target = secondaries[_secondaryIndex];
            }
        }

        _lastDesiredScene = target?.ObsSceneName;
        if (target is null) return;

        // While a hand is live, don't re-send the same scene name over and over —
        // OBS ignores duplicates but we want to avoid log noise. Between hands we
        // *do* re-fire so rotation works even when the same secondary comes up twice.
        if (handInProgress && string.Equals(target.ObsSceneName, _lastAppliedScene, StringComparison.Ordinal))
            return;

        // Debounce rapid StateChanged bursts (mid-deal card flicker).
        var since = DateTimeOffset.UtcNow - _lastSwitchAt;
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, settings.SwitchDebounceMs));
        if (since < debounce)
        {
            try { await Task.Delay(debounce - since, ct); }
            catch (OperationCanceledException) { return; }
        }

        if (await obs.SetSceneAsync(target.ObsSceneName, ct))
        {
            _lastAppliedScene = target.ObsSceneName;
            _lastSwitchAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Switched OBS scene to {Scene} (role={Role}, handInProgress={Live}).",
                target.ObsSceneName, target.Role, handInProgress);
        }
    }

    /// <summary>
    /// A hand is "in progress" iff there is at least one non-folded player and every
    /// non-folded player that has been dealt any card holds exactly 2 hole cards.
    /// Uses the latched pair so brief RFID drops don't kick us off the main camera.
    /// </summary>
    private bool IsHandInProgress()
    {
        var active = tableState.Players.Where(p => !p.IsFolded).ToList();
        if (active.Count == 0) return false;

        var dealtIn = active.Where(p => p.HoleCards.Count > 0 || p.DealtThisHand.Count > 0).ToList();
        if (dealtIn.Count == 0) return false;

        return dealtIn.All(p => (p.LatchedHoleCards?.Count ?? p.HoleCards.Count) == 2);
    }
}

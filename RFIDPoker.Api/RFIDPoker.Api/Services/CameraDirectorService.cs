using RFIDPoker.Api.Models;
using Microsoft.Extensions.Options;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Live status snapshot exposed to the admin UI.
/// </summary>
public record CameraDirectorStatus(
    bool Enabled,
    bool Connected,
    string? CurrentScene,
    string? DesiredScene,
    bool HandInProgress,
    bool BroadcastLive);

public interface ICameraDirector
{
    CameraDirectorStatus GetStatus();
}

/// <summary>
/// Watches the table state and drives OBS scene switches.
/// - Main scene is selected while a hand is live AND broadcast is live.
/// - Otherwise the enabled secondary cameras are rotated on a fixed interval.
/// - Fully idle when broadcast is stopped (no scene switches sent).
/// </summary>
public class CameraDirectorService(
    ITableStateManager tableState,
    IObsClient obs,
    IBroadcastState broadcast,
    IServiceScopeFactory scopeFactory,
    IOptions<ObsSettings> bootstrapDefaults,
    ILogger<CameraDirectorService> logger) : BackgroundService, ICameraDirector
{
    private readonly SemaphoreSlim _tick = new(0, int.MaxValue);
    private List<Camera> _cameras = [];
    private ObsSettings _settings = bootstrapDefaults.Value;
    private int _secondaryIndex;
    private string? _lastAppliedScene;
    private DateTimeOffset _lastSwitchAt = DateTimeOffset.MinValue;
    private bool _lastHandInProgress;
    private string? _lastDesiredScene;

    public CameraDirectorStatus GetStatus() => new(
        Enabled: _settings.Enabled,
        Connected: obs.IsConnected,
        CurrentScene: obs.CurrentScene,
        DesiredScene: _lastDesiredScene,
        HandInProgress: _lastHandInProgress,
        BroadcastLive: broadcast.IsLive);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadCamerasAsync(stoppingToken);
        await LoadSettingsAsync(stoppingToken);

        void OnStateChanged() => _tick.Release();
        void OnCamerasChanged() { _ = LoadCamerasAsync(stoppingToken); _tick.Release(); }
        void OnSettingChanged(string key)
        {
            if (key == SettingKeys.Obs)
            {
                _ = LoadSettingsAsync(stoppingToken);
                _tick.Release();
            }
        }
        void OnBroadcastChanged() => _tick.Release();

        tableState.StateChanged += OnStateChanged;
        tableState.HandReset += OnStateChanged;
        broadcast.Changed += OnBroadcastChanged;

        using (var scope = scopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICameraRepository>().Changed += OnCamerasChanged;
            scope.ServiceProvider.GetRequiredService<ISettingsStore>().Changed += OnSettingChanged;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var rotate = TimeSpan.FromSeconds(Math.Max(1, _settings.SecondaryRotationSeconds));
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
            broadcast.Changed -= OnBroadcastChanged;
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

    private async Task LoadSettingsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
            _settings = await store.GetAsync(SettingKeys.Obs, bootstrapDefaults.Value, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OBS settings; using previous.");
        }
    }

    private async Task ApplyAsync(CancellationToken ct)
    {
        if (!_settings.Enabled) return;
        if (!broadcast.IsLive) { _lastDesiredScene = null; return; }

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

        if (handInProgress && string.Equals(target.ObsSceneName, _lastAppliedScene, StringComparison.Ordinal))
            return;

        var since = DateTimeOffset.UtcNow - _lastSwitchAt;
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _settings.SwitchDebounceMs));
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
    /// A hand is considered "in progress" (main camera live) once the flop has been
    /// dealt. Pre-flop stays on the secondary rotation. Also requires at least one
    /// non-folded player still holding cards, so an end-of-hand board sitting on the
    /// felt with no live players doesn't hold the main camera.
    /// </summary>
    private bool IsHandInProgress()
    {
        if (tableState.CommunityCards.Count < 3) return false;

        var active = tableState.Players.Where(p => !p.IsFolded).ToList();
        if (active.Count == 0) return false;

        return active.Any(p =>
            p.HoleCards.Count > 0
            || (p.LatchedHoleCards?.Count ?? 0) > 0
            || p.DealtThisHand.Count > 0);
    }
}

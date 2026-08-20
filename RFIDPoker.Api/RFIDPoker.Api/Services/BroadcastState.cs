using Microsoft.Extensions.Hosting;

namespace RFIDPoker.Api.Services;

public interface IBroadcastState
{
    bool IsLive { get; }
    event Action? Changed;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>
/// Master on/off switch for the pipeline. When off, RFID reads are discarded, the
/// analysis engine skips work, the camera director stays idle, and the auto-reset
/// / auto-fold services no-op. Persisted to <see cref="ISettingsStore"/> so a
/// crash mid-broadcast comes back live.
/// </summary>
public class BroadcastState(
    IServiceScopeFactory scopeFactory,
    ITableStateManager tableState,
    ILogger<BroadcastState> logger) : IBroadcastState, IHostedService
{
    private volatile bool _isLive;
    public bool IsLive => _isLive;

    public event Action? Changed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
            _isLive = await settings.GetAsync(SettingKeys.BroadcastLive, false, cancellationToken);
            logger.LogInformation("Broadcast state loaded from store: IsLive={IsLive}", _isLive);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load broadcast state; defaulting to off.");
            _isLive = false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    async Task IBroadcastState.StartAsync(CancellationToken ct)
    {
        if (_isLive) return;
        _isLive = true;
        await PersistAsync(true, ct);
        // Fresh hand on go-live so any lingering state from prior processing is wiped.
        try { tableState.NewHand(); }
        catch (Exception ex) { logger.LogWarning(ex, "NewHand() on broadcast start failed."); }
        logger.LogInformation("Broadcast started.");
        Changed?.Invoke();
    }

    async Task IBroadcastState.StopAsync(CancellationToken ct)
    {
        if (!_isLive) return;
        _isLive = false;
        await PersistAsync(false, ct);
        // Wipe everything so the HUD doesn't show stale cards while off-air.
        try { tableState.NewHand(); }
        catch (Exception ex) { logger.LogWarning(ex, "NewHand() on broadcast stop failed."); }
        logger.LogInformation("Broadcast stopped.");
        Changed?.Invoke();
    }

    private async Task PersistAsync(bool value, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
            await settings.SetAsync(SettingKeys.BroadcastLive, value, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist broadcast state.");
        }
    }
}

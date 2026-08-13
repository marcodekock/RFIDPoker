using Microsoft.Extensions.Options;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Fires <see cref="ITableStateManager.NewHand"/> when a hand that was previously active
/// has had no cards on the table (community, hole, or muck) for a configurable idle window.
/// </summary>
public class IdleHandResetService(
    ITableStateManager tableState,
    IOptions<RfidConfig> rfidOptions,
    ILogger<IdleHandResetService> logger) : BackgroundService
{
    private readonly TimeSpan _idleWindow = TimeSpan.FromMilliseconds(
        Math.Max(500, rfidOptions.Value.IdleHandResetMs));

    private bool _handWasActive;
    private DateTimeOffset? _emptySince;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromMilliseconds(500);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Idle hand reset check failed.");
            }

            try { await Task.Delay(poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Tick()
    {
        // "Cards present" for the purpose of idle detection means unfolded seats still
        // holding cards, or a live board. Folded players preserve their hole cards for
        // display but shouldn't keep the hand alive; the muck also shouldn't.
        var hasAnyCards =
            tableState.CommunityCards.Count > 0
            || tableState.Players.Any(p => !p.IsFolded && p.HoleCards.Count > 0);

        if (hasAnyCards)
        {
            _handWasActive = true;
            _emptySince = null;
            return;
        }

        if (!_handWasActive) return;

        _emptySince ??= DateTimeOffset.UtcNow;

        if (DateTimeOffset.UtcNow - _emptySince.Value >= _idleWindow)
        {
            logger.LogInformation("Table idle for {Ms}ms after active hand; resetting.", _idleWindow.TotalMilliseconds);
            tableState.NewHand();
            _handWasActive = false;
            _emptySince = null;
        }
    }
}

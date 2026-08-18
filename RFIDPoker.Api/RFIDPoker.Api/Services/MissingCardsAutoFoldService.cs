using Microsoft.Extensions.Options;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Auto-folds any dealt-in seat that has gone <see cref="RfidConfig.MissingCardsFoldMs"/>
/// without cards on its seat antenna, and moves their preserved hole cards to the muck.
/// If the cards reappear before the timeout, <see cref="TableStateManager.SetPlayerHoleCards"/>
/// clears the timer.
/// </summary>
public class MissingCardsAutoFoldService(
    ITableStateManager tableState,
    IOptions<RfidConfig> rfidOptions,
    ILogger<MissingCardsAutoFoldService> logger) : BackgroundService
{
    private readonly TimeSpan _threshold = TimeSpan.FromMilliseconds(
        Math.Max(500, rfidOptions.Value.MissingCardsFoldMs));

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
                logger.LogWarning(ex, "Missing-cards auto-fold check failed.");
            }

            try { await Task.Delay(poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        // Snapshot so we can mutate without holding the manager's lock.
        var candidates = tableState.Players
            .Where(p => !p.IsFolded
                && p.CardsMissingSince.HasValue
                && now - p.CardsMissingSince.Value >= _threshold
                && (p.HoleCards.Count > 0 || p.DealtThisHand.Count > 0))
            .Select(p => (p.SeatNumber, Cards: p.HoleCards.Count > 0
                ? p.HoleCards.ToList()
                : p.DealtThisHand.ToList()))
            .ToList();

        foreach (var (seat, cards) in candidates)
        {
            logger.LogInformation("Seat {Seat} auto-folded after {Ms}ms without cards; moving {Count} card(s) to muck.",
                seat, _threshold.TotalMilliseconds, cards.Count);
            tableState.FoldPlayer(seat);
            if (cards.Count > 0)
            {
                tableState.AddMuckedCards(cards);
                // Preserve the cards on the seat so they're still shown as folded.
                tableState.SetPlayerHoleCards(seat, cards);
            }
        }
    }
}

namespace RFIDPoker.Api.Services;

/// <summary>
/// While a break is active, periodically nudges the tournament state's StateChanged
/// event so the analysis engine rebroadcasts a snapshot with a fresh
/// <c>RemainingSeconds</c>. Sleeps when no break is running.
/// </summary>
public sealed class BreakTickService(ITournamentStateManager tournament) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var snap = tournament.GetBreakSnapshot();
            if (snap is null || !snap.IsActive || snap.IsPaused)
            {
                try { await Task.Delay(500, stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try { await Task.Delay(1000, stoppingToken); }
            catch (OperationCanceledException) { return; }

            tournament.Tick();
        }
    }
}

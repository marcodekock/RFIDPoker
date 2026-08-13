using Microsoft.AspNetCore.SignalR;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Hubs;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface IPokerAnalysisEngine
{
    AnalysisResultDto? GetLatestResult();
}

public class PokerAnalysisEngine(
    ITableStateManager tableState,
    IHandEvaluator handEvaluator,
    IEquityCalculator equityCalculator,
    IHubContext<AnalysisHub> hubContext,
    ILogger<PokerAnalysisEngine> logger) : BackgroundService, IPokerAnalysisEngine
{
    private readonly Channel _channel = new();
    private AnalysisResultDto? _latestResult;
    private CancellationTokenSource? _calculationCts;

    public AnalysisResultDto? GetLatestResult() => _latestResult;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        tableState.StateChanged += OnStateChanged;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        tableState.StateChanged -= OnStateChanged;
        return base.StopAsync(cancellationToken);
    }

    private void OnStateChanged()
    {
        _channel.Signal();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _channel.WaitAsync(stoppingToken);

            // Cancel any in-progress calculation
            _calculationCts?.Cancel();
            _calculationCts?.Dispose();
            _calculationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            try
            {
                await RunAnalysisAsync(_calculationCts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during poker analysis");
            }
        }
    }

    private async Task RunAnalysisAsync(CancellationToken ct)
    {
        // Small debounce to coalesce rapid state changes
        await Task.Delay(50, ct);

        var activePlayers = tableState.GetActivePlayers();
        var foldedPlayers = tableState.GetFoldedPlayers();
        var communityCards = tableState.CommunityCards.ToList();
        var street = tableState.CurrentStreet;

        var playerAnalyses = new List<PlayerAnalysisDto>();
        var foldedAnalyses = new List<PlayerAnalysisDto>();

        // Evaluate hands for active players
        foreach (var player in activePlayers)
        {
            var hand = handEvaluator.EvaluateBestHand(player.HoleCards, communityCards);
            playerAnalyses.Add(new PlayerAnalysisDto
            {
                SeatNumber = player.SeatNumber,
                PlayerName = player.Name,
                HoleCards = player.HoleCards.ToList(),
                HandRank = hand?.Rank,
                HandDescription = hand?.Description ?? string.Empty,
                BestFiveCards = hand?.BestFiveCards ?? [],
                IsFolded = false,
                IsDealer = player.IsDealer
            });
        }

        foreach (var player in foldedPlayers)
        {
            foldedAnalyses.Add(new PlayerAnalysisDto
            {
                SeatNumber = player.SeatNumber,
                PlayerName = player.Name,
                HoleCards = player.HoleCards.ToList(),
                IsFolded = true,
                IsDealer = player.IsDealer
            });
        }

        // Calculate equity if we have at least 2 active players with hole cards
        var playersWithCards = activePlayers.Where(p => p.HoleCards.Count == 2).ToList();
        Dictionary<int, EquityResult>? equity = null;

        if (playersWithCards.Count >= 2)
        {
            equity = await equityCalculator.CalculateEquityAsync(playersWithCards, communityCards, cancellationToken: ct);
        }

        // Apply equity results
        if (equity is not null)
        {
            for (int i = 0; i < playerAnalyses.Count; i++)
            {
                if (equity.TryGetValue(playerAnalyses[i].SeatNumber, out var eq))
                {
                    playerAnalyses[i] = playerAnalyses[i] with
                    {
                        WinPercentage = Math.Round(eq.WinPercentage, 2),
                        TiePercentage = Math.Round(eq.TiePercentage, 2),
                        LosePercentage = Math.Round(eq.LosePercentage, 2)
                    };
                }
            }
        }

        var result = new AnalysisResultDto
        {
            CurrentStreet = street,
            CommunityCards = communityCards,
            ActivePlayers = playerAnalyses,
            FoldedPlayers = foldedAnalyses,
            ActivePlayerCount = activePlayers.Count,
            Timestamp = DateTimeOffset.UtcNow
        };

        _latestResult = result;

        await hubContext.Clients.All.SendAsync("AnalysisUpdated", result, ct);
    }

    /// <summary>Simple signaling mechanism to wake up the background loop.</summary>
    private sealed class Channel
    {
        private readonly SemaphoreSlim _semaphore = new(0);

        public void Signal()
        {
            // Release only if nobody is waiting to avoid unbounded growth
            if (_semaphore.CurrentCount == 0)
                _semaphore.Release();
        }

        public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);
    }
}

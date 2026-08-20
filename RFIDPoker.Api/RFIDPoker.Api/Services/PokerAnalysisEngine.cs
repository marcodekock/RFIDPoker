using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
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
    ITournamentStateManager tournamentState,
    IHandEvaluator handEvaluator,
    IEquityCalculator equityCalculator,
    IBroadcastState broadcast,
    ITournamentDirectorState tournamentDirector,
    IManualTournamentState manualTournament,
    IHubContext<AnalysisHub> hubContext,
    IOptions<RfidConfig> rfidOptions,
    ILogger<PokerAnalysisEngine> logger) : BackgroundService, IPokerAnalysisEngine
{
    private readonly Channel _channel = new();
    private AnalysisResultDto? _latestResult;
    private CancellationTokenSource? _calculationCts;
    private readonly int _debounceMs = Math.Max(0, rfidOptions.Value.AnalysisDebounceMs);

    public AnalysisResultDto? GetLatestResult() => _latestResult;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        tableState.StateChanged += OnStateChanged;
        tournamentState.StateChanged += OnStateChanged;
        broadcast.Changed += OnStateChanged;
        tournamentDirector.Changed += OnStateChanged;
        manualTournament.Changed += OnStateChanged;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        tableState.StateChanged -= OnStateChanged;
        tournamentState.StateChanged -= OnStateChanged;
        broadcast.Changed -= OnStateChanged;
        tournamentDirector.Changed -= OnStateChanged;
        manualTournament.Changed -= OnStateChanged;
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
            // Wait for the first signal in a burst.
            await _channel.WaitAsync(stoppingToken);

            // Debounce: keep resetting the timer while further changes arrive within the window.
            await DebounceAsync(stoppingToken);

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

    private async Task DebounceAsync(CancellationToken ct)
    {
        if (_debounceMs <= 0) return;

        while (!ct.IsCancellationRequested)
        {
            using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delayTask = Task.Delay(_debounceMs, iterationCts.Token);
            var signalTask = _channel.WaitAsync(iterationCts.Token);

            var finished = await Task.WhenAny(delayTask, signalTask);

            // Cancel and observe the loser so we don't leak a zombie waiter on the
            // semaphore (which would silently swallow future signals).
            iterationCts.Cancel();
            try { await Task.WhenAll(delayTask, signalTask); }
            catch (OperationCanceledException) { }

            if (finished == delayTask) return; // no new signal within window
            // else another state change arrived; loop and reset the timer
        }
    }

    private async Task RunAnalysisAsync(CancellationToken ct)
    {
        // TD-driven overrides. When TD is enabled and has published a snapshot,
        // blinds / break state / player-count are sourced from it and the operator's
        // manual editors are ignored.
        var td = BuildTournamentSnapshot();
        var blindsOverride = td is not null ? FormatBlinds(td) : tableState.Blinds;
        var breakOverride = BuildBreakOverride(td);
        var isOnBreak = breakOverride?.IsActive == true || tournamentState.IsOnBreak;

        // Master off-air switch: while broadcast is stopped, publish a cold snapshot
        // (no cards, no players) so overlay + display look completely idle.
        if (!broadcast.IsLive)
        {
            var offAir = new AnalysisResultDto
            {
                CurrentStreet = tableState.CurrentStreet,
                Blinds = blindsOverride,
                CommunityCards = [],
                MuckedCards = [],
                ActivePlayers = [],
                FoldedPlayers = [],
                ActivePlayerCount = 0,
                HeadsUpOuts = null,
                Break = null,
                Tournament = td,
                Timestamp = DateTimeOffset.UtcNow
            };
            _latestResult = offAir;
            await hubContext.Clients.All.SendAsync("AnalysisUpdated", offAir, ct);
            return;
        }

        // While on break, freeze the table: publish an empty snapshot with the break
        // state so overlay/display hide cards and show the countdown.
        if (isOnBreak)
        {
            var breakSnap = breakOverride ?? tournamentState.GetBreakSnapshot();
            var breakResult = new AnalysisResultDto
            {
                CurrentStreet = tableState.CurrentStreet,
                Blinds = blindsOverride,
                CommunityCards = [],
                MuckedCards = [],
                ActivePlayers = tableState.Players
                    .OrderBy(p => p.SeatNumber)
                    .Select(p => new PlayerAnalysisDto
                    {
                        SeatNumber = p.SeatNumber,
                        PlayerName = p.Name,
                        ChipCount = p.ChipCount,
                        HoleCards = [],
                        IsFolded = false
                    }).ToList(),
                FoldedPlayers = [],
                ActivePlayerCount = td?.PlayersLeft ?? 0,
                HeadsUpOuts = null,
                Break = breakSnap,
                Tournament = td,
                Timestamp = DateTimeOffset.UtcNow
            };
            _latestResult = breakResult;
            await hubContext.Clients.All.SendAsync("AnalysisUpdated", breakResult, ct);
            return;
        }

        // Only seats that currently hold cards are "in the hand". Seats that were seen
        // in a previous hand but weren't dealt in this time (player sat out / knocked out)
        // must be ignored so equity still computes for the remaining players.
        var activePlayers = tableState.GetActivePlayers()
            .Where(p => p.HoleCards.Count > 0)
            .ToList();
        var foldedPlayers = tableState.GetFoldedPlayers()
            .Where(p => p.HoleCards.Count > 0)
            .ToList();
        var communityCards = tableState.CommunityCards.ToList();
        var street = tableState.CurrentStreet;
        var muckedCards = tableState.MuckedCards.ToList();

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
                ChipCount = player.ChipCount,
                HoleCards = player.HoleCards.ToList(),
                HandRank = hand?.Rank,
                HandDescription = hand?.Description ?? string.Empty,
                BestFiveCards = hand?.BestFiveCards ?? [],
                IsFolded = false
            });
        }

        foreach (var player in foldedPlayers)
        {
            foldedAnalyses.Add(new PlayerAnalysisDto
            {
                SeatNumber = player.SeatNumber,
                PlayerName = player.Name,
                ChipCount = player.ChipCount,
                HoleCards = player.HoleCards.ToList(),
                IsFolded = true
            });
        }

        // Calculate equity only when every active player has both hole cards.
        // Otherwise we'd burn CPU (and mislead the display) on a half-dealt table.
        var playersWithCards = activePlayers.Where(p => p.HoleCards.Count == 2).ToList();
        var allPlayersDealt = activePlayers.Count >= 2
            && playersWithCards.Count == activePlayers.Count;
        Dictionary<int, EquityResult>? equity = null;

        // Heads-up outs: compute once and reuse on both interim + final broadcasts so the
        // top-left tab doesn't flicker between updates.
        var headsUpOuts = ComputeHeadsUpOuts(activePlayers, foldedPlayers, communityCards, muckedCards);

        // Always publish an interim result immediately so cards appear on the UI
        // without waiting for equity. Equity fills in on the follow-up broadcast.
        var interim = new AnalysisResultDto
        {
            CurrentStreet = street,
            Blinds = blindsOverride,
            CommunityCards = communityCards,
            MuckedCards = muckedCards,
            ActivePlayers = playerAnalyses,
            FoldedPlayers = foldedAnalyses,
            ActivePlayerCount = td?.PlayersLeft ?? activePlayers.Count,
            HeadsUpOuts = headsUpOuts,
            Break = breakOverride ?? tournamentState.GetBreakSnapshot(),
            Tournament = td,
            Timestamp = DateTimeOffset.UtcNow
        };
        _latestResult = interim;
        await hubContext.Clients.All.SendAsync("AnalysisUpdated", interim, ct);

        if (allPlayersDealt)
        {
            // Folded players' hole cards + anything on the muck antenna are dead — they can't come back on the board.
            var deadCards = foldedPlayers.SelectMany(p => p.HoleCards).Concat(muckedCards);
            equity = await equityCalculator.CalculateEquityAsync(
                playersWithCards, communityCards, deadCards, cancellationToken: ct);
        }

        // Apply equity results
        if (equity is not null)
        {
            for (int i = 0; i < playerAnalyses.Count; i++)
            {
                if (equity.TryGetValue(playerAnalyses[i].SeatNumber, out var eq))
                {
                    var win = (int)Math.Round(eq.WinPercentage);
                    var tie = (int)Math.Round(eq.TiePercentage);
                    playerAnalyses[i] = playerAnalyses[i] with
                    {
                        WinPercentage = win,
                        TiePercentage = tie,
                        LosePercentage = Math.Max(0, 100 - win - tie)
                    };
                }
            }
        }

        var result = new AnalysisResultDto
        {
            CurrentStreet = street,
            Blinds = blindsOverride,
            CommunityCards = communityCards,
            MuckedCards = muckedCards,
            ActivePlayers = playerAnalyses,
            FoldedPlayers = foldedAnalyses,
            ActivePlayerCount = td?.PlayersLeft ?? activePlayers.Count,
            HeadsUpOuts = headsUpOuts,
            Break = breakOverride ?? tournamentState.GetBreakSnapshot(),
            Tournament = td,
            Timestamp = DateTimeOffset.UtcNow
        };

        _latestResult = result;

        await hubContext.Clients.All.SendAsync("AnalysisUpdated", result, ct);
    }

    /// <summary>
    /// When exactly two players remain in the hand and the board is on the flop or turn,
    /// determine which player is behind and enumerate the cards that would flip the lead
    /// to them on the next street.
    /// </summary>
    private HeadsUpOutsDto? ComputeHeadsUpOuts(
        List<Player> activePlayers,
        List<Player> foldedPlayers,
        List<Card> communityCards,
        List<Card> muckedCards)
    {
        if (activePlayers.Count != 2) return null;
        if (communityCards.Count is not (3 or 4)) return null;

        var p1 = activePlayers[0];
        var p2 = activePlayers[1];
        if (p1.HoleCards.Count != 2 || p2.HoleCards.Count != 2) return null;

        var h1 = handEvaluator.EvaluateBestHand(p1.HoleCards, communityCards);
        var h2 = handEvaluator.EvaluateBestHand(p2.HoleCards, communityCards);
        if (h1 is null || h2 is null) return null;

        var cmp = HandEvaluator.CompareHands(h1, h2);
        if (cmp == 0) return null; // tied - nobody is "behind"

        var behind = cmp < 0 ? p1 : p2;
        var leader = cmp < 0 ? p2 : p1;

        // Every card that's already accounted for (visible or known-dead) can't be an out.
        var dead = new HashSet<Card>();
        foreach (var c in communityCards) dead.Add(c);
        foreach (var c in p1.HoleCards) dead.Add(c);
        foreach (var c in p2.HoleCards) dead.Add(c);
        foreach (var p in foldedPlayers)
            foreach (var c in p.HoleCards) dead.Add(c);
        foreach (var c in muckedCards) dead.Add(c);

        var outs = new List<Card>();
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                var candidate = new Card(rank, suit);
                if (dead.Contains(candidate)) continue;

                var nextBoard = new List<Card>(communityCards) { candidate };
                var behindHand = handEvaluator.EvaluateBestHand(behind.HoleCards, nextBoard);
                var leaderHand = handEvaluator.EvaluateBestHand(leader.HoleCards, nextBoard);
                if (behindHand is null || leaderHand is null) continue;

                // An out is a card that makes the behind player strictly beat the leader.
                if (HandEvaluator.CompareHands(behindHand, leaderHand) > 0)
                    outs.Add(candidate);
            }
        }

        if (outs.Count == 0) return null;

        // Sort high rank first, then by suit for a stable, readable display.
        outs = outs
            .OrderByDescending(c => c.Rank)
            .ThenBy(c => c.Suit)
            .ToList();

        return new HeadsUpOutsDto
        {
            SeatNumber = behind.SeatNumber,
            PlayerName = behind.Name,
            Outs = outs
        };
    }

    /// <summary>
    /// Builds the TD snapshot DTO from the latest webhook payload, computing avg stack
    /// (TD sends total chips in <c>ChipCount</c>). Returns null when TD is disabled or
    /// no payload has been received yet.
    /// </summary>
    private TournamentDirectorSnapshotDto? BuildTournamentSnapshot()
    {
        if (tournamentDirector.IsEnabled)
        {
            var u = tournamentDirector.Latest;
            if (u is null) return BuildManualSnapshot();

            var players = Math.Max(0, u.PlayersLeft);
            var avg = players > 0 ? u.ChipCount / players : 0L;

            return new TournamentDirectorSnapshotDto
            {
                Level = u.Level,
                PlayersLeft = players,
                TotalChips = u.ChipCount,
                AverageStack = avg,
                SmallBlind = u.SmallBlind,
                BigBlind = u.BigBlind,
                NextSmallBlind = u.NextSmallBlind,
                NextBigBlind = u.NextBigBlind,
                IsBreak = u.IsBreak == 1,
                NextIsBreak = u.NextIsBreak == 1,
                SecondsLeft = u.SecondsLeft,
                LevelDuration = u.LevelDuration,
                ClockPaused = u.ClockPaused == 1,
                ReceivedAtUtc = tournamentDirector.LastUpdatedUtc ?? DateTimeOffset.UtcNow
            };
        }

        return BuildManualSnapshot();
    }

    private TournamentDirectorSnapshotDto? BuildManualSnapshot()
    {
        var m = manualTournament.Current;
        if (m is null || !m.HasAny) return null;

        var players = Math.Max(0, m.PlayersLeft);
        var avg = players > 0 && m.TotalChips > 0 ? m.TotalChips / players : 0L;

        return new TournamentDirectorSnapshotDto
        {
            Level = m.Level,
            PlayersLeft = players,
            TotalChips = m.TotalChips,
            AverageStack = avg,
            SmallBlind = m.SmallBlind,
            BigBlind = m.BigBlind,
            NextSmallBlind = m.NextSmallBlind,
            NextBigBlind = m.NextBigBlind,
            IsBreak = false,
            NextIsBreak = false,
            SecondsLeft = 0,
            LevelDuration = 0,
            ClockPaused = false,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string? FormatBlinds(TournamentDirectorSnapshotDto td)
    {
        if (td.SmallBlind <= 0 && td.BigBlind <= 0) return null;
        return $"{td.SmallBlind:N0} / {td.BigBlind:N0}";
    }

    /// <summary>
    /// Synthesizes a BreakStateDto from the TD payload when TD is on break, so the
    /// existing overlay/display break UI just works. Returns null when TD isn't
    /// signalling a break — the manual break state stays in charge.
    /// </summary>
    private static BreakStateDto? BuildBreakOverride(TournamentDirectorSnapshotDto? td)
    {
        if (td is null || !td.IsBreak) return null;
        return new BreakStateDto
        {
            IsActive = true,
            IsPaused = td.ClockPaused,
            Label = "Break",
            TotalSeconds = Math.Max(td.LevelDuration, td.SecondsLeft),
            RemainingSeconds = Math.Max(0, td.SecondsLeft),
            ServerNowUtc = DateTimeOffset.UtcNow
        };
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

using Microsoft.Extensions.Options;
using RFIDPoker.Emulator.Options;

namespace RFIDPoker.Emulator.Services;

/// <summary>
/// Drives the emulated table: shuffles a deck, deals hole cards to each seat, then
/// flop/turn/river, then mucks everything and starts again. Every "physical" action is
/// simply a call to <see cref="ITagEmitter"/>, whose state is broadcast to any connected
/// WebSocket clients by <see cref="EmulatedReaderHost"/>.
/// </summary>
public sealed class HandSimulator(
    ITagEmitter emitter,
    IOptions<EmulatorConfig> options,
    ILogger<HandSimulator> logger) : BackgroundService
{
    private readonly Random _rng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = options.Value;
        if (cfg.PlayerCount < 1)
        {
            logger.LogWarning("Emulator:PlayerCount is {Count}; nothing to deal.", cfg.PlayerCount);
            return;
        }

        // Small warm-up so seeding + first WebSocket client can connect before dealing.
        try { await Task.Delay(cfg.PreDealDelayMs, stoppingToken); }
        catch (OperationCanceledException) { return; }

        do
        {
            try
            {
                await RunOneHandAsync(cfg, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Emulated hand failed; restarting.");
            }

            try { await Task.Delay(cfg.BetweenHandsMs, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
        while (cfg.AutoLoopHands && !stoppingToken.IsCancellationRequested);
    }

    private async Task RunOneHandAsync(EmulatorConfig cfg, CancellationToken ct)
    {
        emitter.ClearAll();

        var deck = Deck.Shuffled(_rng);
        var pos = 0;
        var deal = TimeSpan.FromMilliseconds(Math.Max(0, cfg.BetweenCardDealMs));

        // Assign seats to consecutive antenna indices starting at SeatAntennaStart,
        // skipping any indices reserved for muck / flop / turn-river. This lets the
        // configured antenna layout (Flop/Turn/Muck) stay fixed while PlayerCount
        // scales up to 9 seats.
        var reserved = new HashSet<int>(cfg.FlopAntennas) { cfg.TurnRiverAntenna, cfg.MuckAntenna };
        var seatAntennas = new List<int>(cfg.PlayerCount);
        var next = cfg.SeatAntennaStart;
        while (seatAntennas.Count < cfg.PlayerCount)
        {
            if (!reserved.Contains(next)) seatAntennas.Add(next);
            next++;
        }

        // Track what's on each seat so we can muck the correct tags on fold.
        var seatCards = new Dictionary<int, List<EmuCard>>();
        foreach (var ant in seatAntennas) seatCards[ant] = [];

        logger.LogInformation("=== New hand: {Players} players ===", cfg.PlayerCount);

        // Two rounds of hole-card dealing (poker style), one card per seat per round.
        for (var round = 0; round < 2; round++)
        {
            foreach (var ant in seatAntennas)
            {
                var card = deck[pos++];
                var tag = card.TagId(cfg.TagPrefix);
                emitter.PlaceTag(ant, tag);
                seatCards[ant].Add(card);
                logger.LogDebug("Seat ant {Ant} <- {Card} ({Tag})", ant, card, tag);
                await Task.Delay(deal, ct);
            }
        }

        // Hold pre-flop.
        await Task.Delay(cfg.PreFlopHoldMs, ct);
        await MaybeRandomFoldAsync(cfg, seatCards, ct);

        // Burn 1, deal 3 flop cards across the configured flop antennas.
        pos++;
        var flopAnts = cfg.FlopAntennas.Count > 0 ? cfg.FlopAntennas : [5];
        for (var i = 0; i < 3; i++)
        {
            var card = deck[pos++];
            var ant = flopAnts[i % flopAnts.Count];
            emitter.PlaceTag(ant, card.TagId(cfg.TagPrefix));
            logger.LogDebug("Flop ant {Ant} <- {Card}", ant, card);
            await Task.Delay(deal, ct);
        }

        await Task.Delay(cfg.FlopHoldMs, ct);
        await MaybeRandomFoldAsync(cfg, seatCards, ct);

        // Burn 1, turn.
        pos++;
        var turn = deck[pos++];
        emitter.PlaceTag(cfg.TurnRiverAntenna, turn.TagId(cfg.TagPrefix));
        logger.LogDebug("Turn ant {Ant} <- {Card}", cfg.TurnRiverAntenna, turn);

        await Task.Delay(cfg.TurnHoldMs, ct);
        await MaybeRandomFoldAsync(cfg, seatCards, ct);

        // Burn 1, river.
        pos++;
        var river = deck[pos++];
        emitter.PlaceTag(cfg.TurnRiverAntenna, river.TagId(cfg.TagPrefix));
        logger.LogDebug("River ant {Ant} <- {Card}", cfg.TurnRiverAntenna, river);

        await Task.Delay(cfg.RiverHoldMs, ct);

        // Muck everything: pull all seat cards and community cards off their antennas
        // and place them on the muck antenna so the API resets the hand cleanly.
        var muckAnt = cfg.MuckAntenna;
        var muckSet = new List<string>();

        foreach (var (ant, cards) in seatCards)
        {
            foreach (var c in cards) muckSet.Add(c.TagId(cfg.TagPrefix));
            emitter.ClearAntenna(ant);
        }
        foreach (var ant in flopAnts) emitter.ClearAntenna(ant);
        emitter.ClearAntenna(cfg.TurnRiverAntenna);

        foreach (var tag in muckSet) emitter.PlaceTag(muckAnt, tag);
        emitter.PlaceTag(muckAnt, turn.TagId(cfg.TagPrefix));
        emitter.PlaceTag(muckAnt, river.TagId(cfg.TagPrefix));

        logger.LogInformation("Hand ended. Mucked {Count} tags.", muckSet.Count + 2);

        // Give the API time to see the muck, then clear so the next hand starts fresh.
        await Task.Delay(1500, ct);
        emitter.ClearAll();
    }

    private async Task MaybeRandomFoldAsync(
        EmulatorConfig cfg,
        Dictionary<int, List<EmuCard>> seatCards,
        CancellationToken ct)
    {
        if (cfg.RandomFoldChance <= 0) return;

        var candidates = seatCards.Where(kv => kv.Value.Count > 0).ToList();
        if (candidates.Count <= 1) return;

        if (_rng.NextDouble() >= cfg.RandomFoldChance) return;

        var (ant, cards) = candidates[_rng.Next(candidates.Count)];
        logger.LogInformation("Random fold: seat ant {Ant}", ant);

        foreach (var c in cards)
        {
            var tag = c.TagId(cfg.TagPrefix);
            emitter.RemoveTag(ant, tag);
            emitter.PlaceTag(cfg.MuckAntenna, tag);
        }
        cards.Clear();

        await Task.Delay(cfg.BetweenCardDealMs, ct);
    }
}

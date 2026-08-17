using Microsoft.Extensions.Options;
using RFIDPoker.Emulator.Options;

namespace RFIDPoker.Emulator.Services;

/// <summary>
/// On startup, POSTs a tag-&gt;card mapping for every card in the deck to the RFIDPoker.Api
/// calibration endpoint, so the emulator's synthesized tag ids are recognized.
/// Runs once and then exits.
/// </summary>
public sealed class MappingSeeder(
    IOptions<EmulatorConfig> options,
    IHttpClientFactory httpFactory,
    ILogger<MappingSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = options.Value;
        if (!cfg.SeedMappingsOnStartup)
        {
            logger.LogInformation("SeedMappingsOnStartup=false; skipping.");
            return;
        }

        var client = httpFactory.CreateClient(nameof(MappingSeeder));
        client.BaseAddress = new Uri(cfg.ApiBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(5);

        // Wait for the API to be reachable.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var probe = await client.GetAsync("api/calibration/mappings", stoppingToken);
                if (probe.IsSuccessStatusCode) break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "API not yet reachable at {Url}; retrying...", cfg.ApiBaseUrl);
            }

            try { await Task.Delay(1000, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        var seeded = 0;
        foreach (var card in Deck.Standard)
        {
            var tagId = card.TagId(cfg.TagPrefix);
            var body = new { tagId, rank = card.Rank, suit = card.Suit };
            try
            {
                var res = await client.PostAsJsonAsync("api/calibration/mappings", body, stoppingToken);
                if (res.IsSuccessStatusCode) seeded++;
                else logger.LogWarning("Seeding {Tag} -> {Rank}/{Suit} failed: {Status}",
                    tagId, card.Rank, card.Suit, res.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to seed mapping for {Tag}.", tagId);
            }
        }

        logger.LogInformation("Seeded {Count}/52 emulator card mappings via {Url}.", seeded, cfg.ApiBaseUrl);
    }
}

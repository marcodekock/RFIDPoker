using System.Net.Http.Headers;
using System.Net.Http.Json;
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

        // Acquire a user JWT so the calibration endpoints (which now require auth) accept us.
        var token = await AcquireTokenAsync(client, cfg, stoppingToken);

        // Wait for the API to be reachable.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "api/calibration/mappings");
                if (token is not null)
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var probe = await client.SendAsync(req, stoppingToken);
                if (probe.IsSuccessStatusCode) break;
                if (probe.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    logger.LogWarning("API returned 401 for calibration probe. Check Emulator:ApiUsername/ApiPassword and that ApiBaseUrl uses https (HttpClient strips Authorization on http->https redirects).");
                    return;
                }
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
                using var req = new HttpRequestMessage(HttpMethod.Post, "api/calibration/mappings")
                {
                    Content = JsonContent.Create(body)
                };
                if (token is not null)
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var res = await client.SendAsync(req, stoppingToken);
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

    private async Task<string?> AcquireTokenAsync(HttpClient client, EmulatorConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.ApiUsername) || string.IsNullOrWhiteSpace(cfg.ApiPassword))
        {
            logger.LogWarning("No Emulator:ApiUsername/ApiPassword configured; API calls will likely 401.");
            return null;
        }

        // Retry login while the API is still starting up.
        for (var attempt = 0; attempt < 30 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                var resp = await client.PostAsJsonAsync("api/auth/login",
                    new { username = cfg.ApiUsername, password = cfg.ApiPassword }, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var payload = await resp.Content.ReadFromJsonAsync<LoginResponse>(
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
                        cancellationToken: ct);
                    if (!string.IsNullOrWhiteSpace(payload?.Token))
                    {
                        logger.LogInformation("Acquired API token for user '{User}'.", cfg.ApiUsername);
                        return payload!.Token;
                    }
                    logger.LogError("Login for '{User}' succeeded but response body had no token. Body: {Body}",
                        cfg.ApiUsername, await resp.Content.ReadAsStringAsync(ct));
                    return null;
                }
                else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    logger.LogError("Login for '{User}' rejected by API (401). Check Emulator:ApiUsername/ApiPassword.", cfg.ApiUsername);
                    return null;
                }
                else
                {
                    logger.LogWarning("Login attempt returned {Status}; retrying...", resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "API login not yet reachable; retrying...");
            }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private sealed record LoginResponse(string Token, string Username, string[] Roles);
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Runtime-editable settings persisted to the SQLite <see cref="AppSetting"/> table.
/// Typed getters/setters serialize objects as JSON so future features can add settings
/// without new tables/migrations.
/// </summary>
public interface ISettingsStore
{
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>Raised whenever a setting is written. Argument is the key.</summary>
    event Action<string>? Changed;
}

public static class SettingKeys
{
    public const string Obs = "obs.settings";
    public const string BroadcastLive = "broadcast.isLive";
    public const string TournamentDirectorEnabled = "tournamentDirector.enabled";
    public const string ManualTournamentInfo = "tournament.manual";
}

public class SettingsStore(AppDbContext db) : ISettingsStore
{
    // Static event because callers (singletons) live longer than the scoped instance.
    private static event Action<string>? StaticChanged;

    public event Action<string>? Changed
    {
        add => StaticChanged += value;
        remove => StaticChanged -= value;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null || string.IsNullOrEmpty(row.Value)) return defaultValue;
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(row.Value, Json);
            return parsed is null ? defaultValue : parsed;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, Json);
        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = json });
        }
        else
        {
            existing.Value = json;
        }
        await db.SaveChangesAsync(ct);
        StaticChanged?.Invoke(key);
    }
}

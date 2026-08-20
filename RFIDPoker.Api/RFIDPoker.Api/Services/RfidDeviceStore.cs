using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Runtime source of truth for MUX/reader configuration. Backed by
/// the <c>RfidDevices</c>/<c>RfidAntennas</c> tables. Consumers snapshot
/// <see cref="Devices"/> and subscribe to <see cref="Changed"/> to react
/// when the operator edits the layout from the Config page.
/// </summary>
public interface IRfidDeviceStore
{
    /// <summary>Current immutable snapshot of configured devices.</summary>
    IReadOnlyList<DeviceConfig> Devices { get; }

    /// <summary>Raised after <see cref="Devices"/> has been replaced.</summary>
    event Action? Changed;

    /// <summary>Reload from the database.</summary>
    Task<IReadOnlyList<DeviceConfig>> ReloadAsync(CancellationToken ct = default);

    /// <summary>Replace the persisted device list with <paramref name="devices"/>.</summary>
    Task ReplaceAsync(IEnumerable<DeviceConfig> devices, CancellationToken ct = default);
}

public class RfidDeviceStore(IServiceScopeFactory scopeFactory, ILogger<RfidDeviceStore> logger) : IRfidDeviceStore
{
    private readonly Lock _lock = new();
    private IReadOnlyList<DeviceConfig> _devices = Array.Empty<DeviceConfig>();

    public IReadOnlyList<DeviceConfig> Devices
    {
        get { lock (_lock) return _devices; }
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<DeviceConfig>> ReloadAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entities = await db.RfidDevices
            .Include(d => d.Antennas)
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var snapshot = entities.Select(ToConfig).ToList();
        lock (_lock) _devices = snapshot;
        Changed?.Invoke();
        return snapshot;
    }

    public async Task ReplaceAsync(IEnumerable<DeviceConfig> devices, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Simple, robust: wipe and reinsert. The dataset is tiny (a handful of MUXes)
        // and this avoids fragile matching against incoming client-side ids.
        var existing = await db.RfidDevices.Include(d => d.Antennas).ToListAsync(ct);
        db.RfidDevices.RemoveRange(existing);
        await db.SaveChangesAsync(ct);

        foreach (var d in devices)
        {
            db.RfidDevices.Add(new RfidDeviceEntity
            {
                Name = d.Name ?? d.WebSocketUrl,
                WebSocketUrl = d.WebSocketUrl,
                Antennas = d.Antennas.Select(a => new RfidAntennaEntity
                {
                    AntennaIndex = a.AntennaIndex,
                    Function = a.Function,
                    SeatNumber = a.SeatNumber
                }).ToList()
            });
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("RFID device layout updated: {Count} device(s).", devices.Count());
        await ReloadAsync(ct);
    }

    private static DeviceConfig ToConfig(RfidDeviceEntity e) => new()
    {
        WebSocketUrl = e.WebSocketUrl,
        Name = string.IsNullOrWhiteSpace(e.Name) ? null : e.Name,
        Antennas = e.Antennas
            .OrderBy(a => a.AntennaIndex)
            .Select(a => new AntennaConfig
            {
                AntennaIndex = a.AntennaIndex,
                Function = a.Function,
                SeatNumber = a.SeatNumber
            })
            .ToList()
    };
}

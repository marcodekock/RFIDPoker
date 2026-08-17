using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RFIDPoker.Emulator.Services;

/// <summary>
/// Represents one Pepper-style UID message: {"type":"uid","uid":"...","antenna":N,...}.
/// </summary>
public sealed record PepperMessage(
    string Type,
    string Uid,
    int Antenna,
    string DeviceName,
    int Sak = 0,
    string String = "",
    bool KnownTag = true);

/// <summary>
/// In-memory model of which tags are "physically present" on which antennas.
/// The <see cref="EmulatedReaderHost"/> reads this every <see cref="Options.EmulatorConfig.TagRepeatIntervalMs"/>
/// and broadcasts UID messages for each present tag to every connected WebSocket client.
/// This mimics how the real Pepper hardware continuously re-emits UIDs while a tag stays on the antenna.
/// </summary>
public interface ITagEmitter
{
    /// <summary>Adds a tag to an antenna. Idempotent.</summary>
    void PlaceTag(int antennaIndex, string tagId);

    /// <summary>Removes a single tag from an antenna. No-op if not present.</summary>
    void RemoveTag(int antennaIndex, string tagId);

    /// <summary>Removes all tags currently present on the given antenna.</summary>
    void ClearAntenna(int antennaIndex);

    /// <summary>Removes all tags on every antenna.</summary>
    void ClearAll();

    /// <summary>Snapshot of currently-present tags, keyed by antenna index.</summary>
    IReadOnlyDictionary<int, IReadOnlyList<string>> Snapshot();
}

/// <summary>
/// Singleton store of "present tags" and the pool of connected WebSocket clients.
/// A hosted background loop pumps UID messages to every client at a fixed interval
/// so the RFIDPoker.Api sees the same steady-stream behavior it gets from real hardware.
/// </summary>
public sealed class TagEmitter : ITagEmitter
{
    private readonly ConcurrentDictionary<int, HashSet<string>> _tagsByAntenna = new();
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly object _sync = new();

    public string DeviceName { get; set; } = "EmulatedPepper";

    public void PlaceTag(int antennaIndex, string tagId)
    {
        lock (_sync)
        {
            var set = _tagsByAntenna.GetOrAdd(antennaIndex, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            set.Add(tagId);
        }
    }

    public void RemoveTag(int antennaIndex, string tagId)
    {
        lock (_sync)
        {
            if (_tagsByAntenna.TryGetValue(antennaIndex, out var set)) set.Remove(tagId);
        }
    }

    public void ClearAntenna(int antennaIndex)
    {
        lock (_sync)
        {
            if (_tagsByAntenna.TryGetValue(antennaIndex, out var set)) set.Clear();
        }
    }

    public void ClearAll()
    {
        lock (_sync)
        {
            foreach (var set in _tagsByAntenna.Values) set.Clear();
        }
    }

    public IReadOnlyDictionary<int, IReadOnlyList<string>> Snapshot()
    {
        lock (_sync)
        {
            return _tagsByAntenna.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.ToList());
        }
    }

    // --- WebSocket registry ---------------------------------------------------

    internal Guid Register(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _sockets[id] = socket;
        return id;
    }

    internal void Unregister(Guid id) => _sockets.TryRemove(id, out _);

    internal IReadOnlyCollection<WebSocket> ActiveSockets => _sockets.Values.ToList();
}

/// <summary>
/// Background service that periodically broadcasts UID messages for each currently-present
/// tag to every connected WebSocket client. Mirrors the real Pepper reader's continuous re-emission.
/// </summary>
public sealed class EmulatedReaderHost(
    TagEmitter emitter,
    Microsoft.Extensions.Options.IOptions<Options.EmulatorConfig> options,
    ILogger<EmulatedReaderHost> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = options.Value;
        var interval = TimeSpan.FromMilliseconds(Math.Max(50, cfg.TagRepeatIntervalMs));

        logger.LogInformation("Emulated Pepper reader broadcasting UID frames every {Ms} ms.", interval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Broadcast iteration failed.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task BroadcastOnceAsync(CancellationToken ct)
    {
        var sockets = emitter.ActiveSockets;
        if (sockets.Count == 0) return;

        var snapshot = emitter.Snapshot();
        if (snapshot.Count == 0) return;

        foreach (var (antenna, tags) in snapshot)
        {
            foreach (var tag in tags)
            {
                var msg = new PepperMessage("uid", tag, antenna, emitter.DeviceName);
                var json = JsonSerializer.Serialize(msg, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);

                foreach (var ws in sockets)
                {
                    if (ws.State != WebSocketState.Open) continue;
                    try
                    {
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to send to a client; will be reaped on next receive.");
                    }
                }
            }
        }
    }
}

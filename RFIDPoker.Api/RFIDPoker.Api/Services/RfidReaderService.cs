using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Hubs;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface IRfidReaderService
{
    /// <summary>Builds a per-antenna readings snapshot matching the configured device layout.</summary>
    List<AntennaReadingDto> GetReadingsSnapshot();
}

/// <summary>
/// Opens one WebSocket per configured Pepper reader (each with its own <see cref="DeviceConfig.WebSocketUrl"/>)
/// and translates streamed UID messages into per-antenna tag sets. Tags are considered removed when no
/// message for a given (device, antenna, uid) has arrived within <see cref="RfidConfig.TagTimeoutMs"/>.
/// The Pepper's "device_name" field is ignored — the connection itself identifies the device.
/// </summary>
public class RfidReaderService(
    IOptions<RfidConfig> options,
    ITableStateManager tableState,
    ICardTagMapper cardMapper,
    IHubContext<AnalysisHub> hubContext,
    ILogger<RfidReaderService> logger) : BackgroundService, IRfidReaderService
{
    private readonly RfidConfig _config = options.Value;

    // Key = "DeviceKey:AntennaIndex"; value = uid -> last-seen UTC timestamp.
    private readonly Dictionary<string, Dictionary<string, DateTime>> _tagsByAntenna =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<string>> _lastNotifiedTags =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IReadOnlyDictionary<string, IReadOnlyList<string>> GetCurrentTagsByAntenna()
    {
        lock (_tagsByAntenna)
        {
            return _tagsByAntenna.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.Keys.ToList());
        }
    }

    public List<AntennaReadingDto> GetReadingsSnapshot()
    {
        var currentTags = GetCurrentTagsByAntenna();
        var readings = new List<AntennaReadingDto>();
        foreach (var device in _config.Devices)
        {
            var deviceKey = GetDeviceKey(device);
            foreach (var antenna in device.Antennas)
            {
                var key = MakeKey(deviceKey, antenna.AntennaIndex);
                var tags = currentTags.TryGetValue(key, out var t) ? t.ToList() : [];
                readings.Add(new AntennaReadingDto(
                    deviceKey,
                    antenna.AntennaIndex,
                    antenna.Function.ToString(),
                    tags));
            }
        }
        return readings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.Devices.Count == 0)
        {
            logger.LogWarning("Rfid:Devices is empty. Reader service will not connect to anything.");
            return;
        }

        var evictionTask = Task.Run(() => EvictionLoopAsync(stoppingToken), stoppingToken);

        var deviceTasks = _config.Devices
            .Select(d => Task.Run(() => RunDeviceLoopAsync(d, stoppingToken), stoppingToken))
            .ToArray();

        try { await Task.WhenAll(deviceTasks); } catch { /* individual loops log their own errors */ }
        try { await evictionTask; } catch { /* ignored */ }
    }

    private async Task RunDeviceLoopAsync(DeviceConfig device, CancellationToken stoppingToken)
    {
        var deviceKey = GetDeviceKey(device);

        if (!Uri.TryCreate(device.WebSocketUrl, UriKind.Absolute, out var uri))
        {
            logger.LogError("Device '{Key}' has invalid WebSocketUrl '{Url}'. Skipping.",
                deviceKey, device.WebSocketUrl);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(device, uri, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WebSocket error for {Device}; will reconnect.", deviceKey);
            }

            if (stoppingToken.IsCancellationRequested) break;

            try { await Task.Delay(_config.ReconnectDelayMs, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunConnectionAsync(DeviceConfig device, Uri uri, CancellationToken ct)
    {
        var deviceKey = GetDeviceKey(device);

        using var ws = new ClientWebSocket();

        logger.LogInformation("Connecting to Pepper '{Device}' at {Url}...", deviceKey, uri);
        await ws.ConnectAsync(uri, ct);
        logger.LogInformation("Connected to Pepper '{Device}'.", deviceKey);

        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (WebSocketException ex)
            {
                logger.LogWarning(ex, "WebSocket receive failed for {Device}.", deviceKey);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                logger.LogInformation("WebSocket closed by server for {Device}.", deviceKey);
                break;
            }

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (!result.EndOfMessage) continue;

            var message = messageBuffer.ToString();
            messageBuffer.Clear();

            foreach (var json in SplitJsonObjects(message))
            {
                TryHandleMessage(device, json);
            }
        }
    }

    private void TryHandleMessage(DeviceConfig device, string json)
    {
        PepperMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<PepperMessage>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Pepper message: {Json}", json);
            return;
        }

        if (msg is null || !string.Equals(msg.Type, "uid", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrEmpty(msg.Uid)) return;

        var deviceKey = GetDeviceKey(device);
        var key = MakeKey(deviceKey, msg.Antenna);
        var uid = msg.Uid.ToUpperInvariant();
        var now = DateTime.UtcNow;

        bool setChanged;
        lock (_tagsByAntenna)
        {
            if (!_tagsByAntenna.TryGetValue(key, out var tags))
            {
                tags = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                _tagsByAntenna[key] = tags;
            }

            var isNew = !tags.ContainsKey(uid);
            tags[uid] = now;
            setChanged = isNew;
        }

        logger.LogDebug("UID from {Device} ant {Antenna}: {Uid}", deviceKey, msg.Antenna, uid);

        if (setChanged)
        {
            NotifyIfChanged(device, msg.Antenna);
        }
    }

    private async Task EvictionLoopAsync(CancellationToken ct)
    {
        // Precompute a lookup so eviction can resolve device+antenna without parsing keys.
        var deviceByKey = _config.Devices.ToDictionary(GetDeviceKey, d => d, StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_config.EvictionIntervalMs, ct); }
            catch (OperationCanceledException) { break; }

            var cutoff = DateTime.UtcNow.AddMilliseconds(-_config.TagTimeoutMs);
            List<string> changedKeys = [];

            lock (_tagsByAntenna)
            {
                foreach (var (key, tags) in _tagsByAntenna)
                {
                    var expired = tags.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
                    if (expired.Count == 0) continue;

                    foreach (var uid in expired) tags.Remove(uid);
                    changedKeys.Add(key);
                }
            }

            foreach (var key in changedKeys)
            {
                var (deviceKey, antenna) = ParseKey(key);
                if (deviceKey is null) continue;
                if (deviceByKey.TryGetValue(deviceKey, out var device))
                {
                    NotifyIfChanged(device, antenna);
                }
            }
        }
    }

    private void NotifyIfChanged(DeviceConfig device, int antennaIndex)
    {
        var deviceKey = GetDeviceKey(device);
        var key = MakeKey(deviceKey, antennaIndex);

        HashSet<string> currentTags;
        lock (_tagsByAntenna)
        {
            currentTags = _tagsByAntenna.TryGetValue(key, out var tags)
                ? new HashSet<string>(tags.Keys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        bool changed;
        lock (_lastNotifiedTags)
        {
            if (!_lastNotifiedTags.TryGetValue(key, out var previous))
                changed = currentTags.Count > 0;
            else
                changed = !previous.SetEquals(currentTags);

            if (changed) _lastNotifiedTags[key] = currentTags;
        }

        if (!changed) return;

        var antenna = device.Antennas.FirstOrDefault(a => a.AntennaIndex == antennaIndex);
        if (antenna is not null)
        {
            ProcessAntennaReading(antenna, currentTags);
        }

        // Push updated readings snapshot to any connected clients.
        _ = BroadcastReadingsAsync();
    }

    private async Task BroadcastReadingsAsync()
    {
        try
        {
            var snapshot = GetReadingsSnapshot();
            await hubContext.Clients.All.SendAsync("ReadingsUpdated", snapshot);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast readings snapshot.");
        }
    }

    private void ProcessAntennaReading(AntennaConfig antenna, HashSet<string> tagIds)
    {
        var cards = tagIds
            .Select(t => cardMapper.GetCard(t))
            .Where(c => c is not null)
            .Cast<Card>()
            .ToList();

        switch (antenna.Function)
        {
            case AntennaFunction.PlayerSeat when antenna.SeatNumber.HasValue:
                if (cards.Count > 0)
                {
                    // Card came back to a seat: revert any accidental muck+fold.
                    var wasMucked = cards.Where(c => tableState.MuckedCards.Contains(c)).ToList();
                    if (wasMucked.Count > 0)
                    {
                        tableState.RemoveMuckedCards(wasMucked);
                        tableState.UnfoldPlayer(antenna.SeatNumber.Value);
                    }
                }
                tableState.SetPlayerHoleCards(antenna.SeatNumber.Value, cards);
                break;

            case AntennaFunction.Flop:
            case AntennaFunction.TurnRiver:
                UpdateCommunityCards();
                break;

            case AntennaFunction.Muck:
                ProcessMuck(cards);
                break;
        }
    }

    private void UpdateCommunityCards()
    {
        // Collect the current set of cards present on any board antenna (flop pair + turn/river).
        // We iterate antennas and (unordered) tag sets, so we can't derive the visual order
        // directly from tag storage — we have to preserve the previous board order below.
        var currentSet = new HashSet<Card>();

        foreach (var device in _config.Devices)
        {
            var deviceKey = GetDeviceKey(device);
            foreach (var antenna in device.Antennas)
            {
                if (antenna.Function is not (AntennaFunction.Flop or AntennaFunction.TurnRiver))
                    continue;

                var key = MakeKey(deviceKey, antenna.AntennaIndex);
                HashSet<string> tags;
                lock (_tagsByAntenna)
                {
                    tags = _tagsByAntenna.TryGetValue(key, out var t)
                        ? new HashSet<string>(t.Keys, StringComparer.OrdinalIgnoreCase)
                        : [];
                }

                foreach (var tagId in tags)
                {
                    var card = cardMapper.GetCard(tagId);
                    if (card is not null) currentSet.Add(card);
                }
            }
        }

        // Rebuild the board preserving the existing order: cards that were already on the
        // board keep their slot; brand-new cards get appended at the end. This is what
        // physically happens (flop -> turn -> river) and prevents the river from being
        // rendered *before* the turn when both share the TurnRiver antenna's hash order.
        var communityCards = new List<Card>(5);
        var placed = new HashSet<Card>();
        foreach (var prev in tableState.CommunityCards)
        {
            if (currentSet.Contains(prev) && placed.Add(prev))
                communityCards.Add(prev);
        }
        foreach (var card in currentSet)
        {
            if (placed.Add(card)) communityCards.Add(card);
        }

        // Auto-detect start of a new hand: board just went from non-empty back to empty
        // AND no unfolded player is currently holding cards. Folded players keep their
        // hole cards for display but must not block the reset.
        var hadBoard = tableState.CommunityCards.Count > 0;
        var anyHoleCards = tableState.Players.Any(p => !p.IsFolded && p.HoleCards.Count > 0);
        if (hadBoard && communityCards.Count == 0 && !anyHoleCards)
        {
            tableState.NewHand();
        }
        else
        {
            tableState.SetCommunityCards(communityCards);
        }
    }

    private void ProcessMuck(List<Card> muckedCards)
    {
        if (muckedCards.Count > 0)
            tableState.AddMuckedCards(muckedCards);

        foreach (var player in tableState.Players)
        {
            if (player.IsFolded) continue;
            // Check both current hole cards AND everything the seat was dealt this hand:
            // by the time a card is read on the muck antenna it may have already left the
            // seat antenna, so HoleCards can be empty. DealtThisHand remembers what came
            // through the seat during this hand.
            var matched = muckedCards
                .Where(c => player.HoleCards.Contains(c) || player.DealtThisHand.Contains(c))
                .ToList();
            if (matched.Count == 0) continue;

            tableState.FoldPlayer(player.SeatNumber);
            // Preserve the folded player's cards so they're shown as folded and counted
            // as dead in equity, even after the physical cards have left the seat.
            var preserved = matched
                .Concat(player.HoleCards.Where(hc => !matched.Contains(hc)))
                .Distinct()
                .ToList();
            tableState.SetPlayerHoleCards(player.SeatNumber, preserved);
        }
    }

    /// <summary>Friendly identifier for a device: Name if set, else host of the WebSocket URL, else the URL itself.</summary>
    public static string GetDeviceKey(DeviceConfig device)
    {
        if (!string.IsNullOrWhiteSpace(device.Name)) return device.Name!;
        if (Uri.TryCreate(device.WebSocketUrl, UriKind.Absolute, out var uri) &&
            !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }
        return device.WebSocketUrl;
    }

    private static string MakeKey(string deviceKey, int antennaIndex)
        => $"{deviceKey}:{antennaIndex}";

    private static (string? deviceKey, int antenna) ParseKey(string key)
    {
        var idx = key.LastIndexOf(':');
        if (idx <= 0 || idx == key.Length - 1) return (null, 0);
        var device = key[..idx];
        return int.TryParse(key[(idx + 1)..], out var ant) ? (device, ant) : (null, 0);
    }

    /// <summary>
    /// Splits a text frame that may contain multiple concatenated JSON objects
    /// (the Pepper does not delimit them). Balances braces while ignoring braces inside strings.
    /// </summary>
    private static IEnumerable<string> SplitJsonObjects(string text)
    {
        var depth = 0;
        var start = -1;
        var inString = false;
        var escape = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escape) { escape = false; }
                else if (c == '\\') { escape = true; }
                else if (c == '"') { inString = false; }
                continue;
            }

            if (c == '"') { inString = true; continue; }

            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return text.Substring(start, i - start + 1);
                    start = -1;
                }
            }
        }
    }

    private sealed class PepperMessage
    {
        [JsonPropertyName("type")]        public string? Type { get; set; }
        [JsonPropertyName("uid")]         public string? Uid { get; set; }
        [JsonPropertyName("sak")]         public int Sak { get; set; }
        [JsonPropertyName("string")]      public string? String { get; set; }
        [JsonPropertyName("device_name")] public string? DeviceName { get; set; }
        [JsonPropertyName("known_tag")]   public bool KnownTag { get; set; }
        [JsonPropertyName("antenna")]     public int Antenna { get; set; }
    }
}

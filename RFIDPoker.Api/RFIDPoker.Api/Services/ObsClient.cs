using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface IObsClient
{
    /// <summary>True if currently connected AND identified.</summary>
    bool IsConnected { get; }

    /// <summary>The most recently switched-to scene, or null if unknown.</summary>
    string? CurrentScene { get; }

    /// <summary>
    /// Switches OBS to the given program scene. Returns true on success; false if the
    /// client is disconnected or OBS rejects the request. Never throws.
    /// </summary>
    Task<bool> SetSceneAsync(string sceneName, CancellationToken ct = default);
}

/// <summary>
/// Minimal OBS WebSocket v5 client (op codes 0/1/2/6/7). Runs a background connect
/// loop that maintains a live session and exposes <see cref="SetSceneAsync"/>.
/// </summary>
public class ObsClient(
    IServiceScopeFactory scopeFactory,
    IOptions<ObsSettings> bootstrapDefaults,
    ILogger<ObsClient> logger) : BackgroundService, IObsClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private volatile bool _identified;
    private CancellationTokenSource? _sessionCts;
    public bool IsConnected => _identified && _socket?.State == WebSocketState.Open;
    public string? CurrentScene { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Signal used to wake the loop when the OBS settings row changes so we
        // (a) reload settings without polling the DB every second and
        // (b) recycle any live session to pick up the new URL/password.
        using var settingsChanged = new SemaphoreSlim(0, 1);

        void OnSettingsChanged(string key)
        {
            if (key != SettingKeys.Obs) return;
            logger.LogInformation("OBS settings changed; recycling session.");
            try { _sessionCts?.Cancel(); } catch { }
            try { settingsChanged.Release(); } catch (SemaphoreFullException) { }
        }

        using (var scope = scopeFactory.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
            store.Changed += OnSettingsChanged;
        }

        try
        {
            // Prime the cached settings once; only reload when notified.
            var settings = await LoadSettingsAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!settings.Enabled)
                {
                    // Sleep until settings change instead of hitting the DB on a timer.
                    try { await settingsChanged.WaitAsync(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    settings = await LoadSettingsAsync(stoppingToken);
                    continue;
                }

                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                try
                {
                    await RunSessionAsync(settings, _sessionCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "OBS session error; will reconnect in {Delay}ms.", settings.ReconnectDelayMs);
                }

                _identified = false;
                try { _socket?.Dispose(); } catch { }
                _socket = null;
                _sessionCts?.Dispose();
                _sessionCts = null;

                // If the session ended because settings changed, reload before reconnecting.
                if (settingsChanged.CurrentCount > 0)
                {
                    try { await settingsChanged.WaitAsync(stoppingToken); } catch (OperationCanceledException) { break; }
                    settings = await LoadSettingsAsync(stoppingToken);
                    continue;
                }

                try { await Task.Delay(Math.Max(500, settings.ReconnectDelayMs), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            // Static event — remove to avoid leaking across service lifetimes.
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
            store.Changed -= OnSettingsChanged;
        }
    }

    private async Task<ObsSettings> LoadSettingsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
            return await store.GetAsync(SettingKeys.Obs, bootstrapDefaults.Value, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load OBS settings from store; using bootstrap defaults.");
            return bootstrapDefaults.Value;
        }
    }

    private async Task RunSessionAsync(ObsSettings settings, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        _socket = socket;

        logger.LogInformation("Connecting to OBS at {Url}", settings.WebSocketUrl);
        await socket.ConnectAsync(new Uri(settings.WebSocketUrl), ct);

        var buffer = new byte[16 * 1024];
        var messageBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            messageBuilder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    logger.LogInformation("OBS closed the connection ({Status}: {Desc}).",
                        result.CloseStatus, result.CloseStatusDescription);
                    return;
                }
                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            await HandleFrameAsync(messageBuilder.ToString(), settings, ct);
        }
    }

    private async Task HandleFrameAsync(string json, ObsSettings settings, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("op", out var opProp)) return;

        var op = opProp.GetInt32();
        var d = doc.RootElement.TryGetProperty("d", out var dProp) ? dProp : default;

        switch (op)
        {
            case 0: // Hello
                await SendIdentifyAsync(d, settings, ct);
                break;
            case 2: // Identified
                _identified = true;
                logger.LogInformation("Identified with OBS.");
                break;
            case 7: // RequestResponse — used for logging errors only.
                if (d.TryGetProperty("requestStatus", out var rs)
                    && rs.TryGetProperty("result", out var okProp)
                    && !okProp.GetBoolean())
                {
                    logger.LogWarning("OBS request failed: {Payload}", d.ToString());
                }
                break;
        }
    }

    private async Task SendIdentifyAsync(JsonElement helloData, ObsSettings settings, CancellationToken ct)
    {
        var rpcVersion = helloData.TryGetProperty("rpcVersion", out var rp) ? rp.GetInt32() : 1;
        string? authString = null;

        if (helloData.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
        {
            var challenge = auth.GetProperty("challenge").GetString() ?? "";
            var salt = auth.GetProperty("salt").GetString() ?? "";
            var password = settings.Password ?? string.Empty;

            // OBS v5 auth: base64(sha256(base64(sha256(password + salt)) + challenge))
            var step1 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
            authString = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(step1 + challenge)));
        }

        var payload = new
        {
            op = 1,
            d = new
            {
                rpcVersion,
                authentication = authString,
                eventSubscriptions = 0 // we don't need events
            }
        };
        await SendJsonAsync(payload, ct);
    }

    public async Task<bool> SetSceneAsync(string sceneName, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            logger.LogDebug("SetSceneAsync({Scene}) skipped — OBS not connected.", sceneName);
            return false;
        }
        if (string.IsNullOrWhiteSpace(sceneName)) return false;

        var payload = new
        {
            op = 6,
            d = new
            {
                requestType = "SetCurrentProgramScene",
                requestId = Guid.NewGuid().ToString("N"),
                requestData = new { sceneName }
            }
        };
        try
        {
            await SendJsonAsync(payload, ct);
            CurrentScene = sceneName;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send SetCurrentProgramScene to OBS.");
            return false;
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open) return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally { _sendLock.Release(); }
    }
}

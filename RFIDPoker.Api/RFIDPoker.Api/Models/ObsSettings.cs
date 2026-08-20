namespace RFIDPoker.Api.Models;

/// <summary>
/// Connection + timing settings for the OBS WebSocket v5 integration used by
/// <see cref="Services.CameraDirectorService"/>. Bound from the "Obs" section
/// of appsettings.json.
/// </summary>
public class ObsSettings
{
    public const string SectionName = "Obs";

    /// <summary>Master switch — when false the director does nothing.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>WebSocket URL of the OBS instance, e.g. "ws://localhost:4455".</summary>
    public string WebSocketUrl { get; set; } = "ws://localhost:4455";

    /// <summary>OBS WebSocket password (leave blank if authentication is disabled).</summary>
    public string? Password { get; set; }

    /// <summary>How long (ms) to wait before reconnecting after a dropped connection.</summary>
    public int ReconnectDelayMs { get; set; } = 3000;

    /// <summary>
    /// How often (seconds) to switch between secondary cameras during dealing / between
    /// hands. Ignored while the main camera is live.
    /// </summary>
    public int SecondaryRotationSeconds { get; set; } = 8;

    /// <summary>
    /// Minimum time (ms) between two scene switches sent to OBS. Prevents thrashing
    /// during noisy StateChanged bursts (e.g. mid-deal card flicker).
    /// </summary>
    public int SwitchDebounceMs { get; set; } = 750;
}

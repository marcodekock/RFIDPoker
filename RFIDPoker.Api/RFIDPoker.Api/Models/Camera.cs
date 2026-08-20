namespace RFIDPoker.Api.Models;

public enum CameraRole
{
    /// <summary>Main "eye in the sky" — shown while a hand is live.</summary>
    Main,

    /// <summary>Rotating cutaway shown between hands / while cards are being dealt.</summary>
    Secondary
}

/// <summary>
/// A configured camera. Cameras are edited at runtime via the admin UI (persisted in
/// the SQLite database); OBS connection settings live in appsettings.json.
/// </summary>
public class Camera
{
    public int Id { get; set; }

    /// <summary>Human-readable label shown in the admin UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The OBS scene name to switch to when this camera is selected.
    /// Must match the scene name exactly as it appears in OBS.
    /// </summary>
    public string ObsSceneName { get; set; } = string.Empty;

    public CameraRole Role { get; set; } = CameraRole.Secondary;

    /// <summary>Sort order for secondary rotation (ignored for Main cameras).</summary>
    public int SortOrder { get; set; }

    /// <summary>If false, the camera is skipped by the director.</summary>
    public bool Enabled { get; set; } = true;
}

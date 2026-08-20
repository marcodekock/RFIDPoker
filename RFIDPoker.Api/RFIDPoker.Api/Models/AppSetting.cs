namespace RFIDPoker.Api.Models;

/// <summary>
/// Simple key/value string store persisted to SQLite. Used for runtime-editable
/// configuration (OBS connection, broadcast state, etc.) that operators need to
/// change without editing appsettings.json + restarting the service.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

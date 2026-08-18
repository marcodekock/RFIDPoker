namespace RFIDPoker.Api.Models;

public enum AntennaFunction
{
    PlayerSeat,
    Flop,
    TurnRiver,
    Muck
}

public class RfidConfig
{
    public const string SectionName = "Rfid";

    /// <summary>
    /// How long (ms) a tag can go unseen before it's considered removed from an antenna.
    /// The Pepper repeatedly emits UID messages while a tag is present; when it stops,
    /// the tag is considered gone after this timeout.
    /// </summary>
    public int TagTimeoutMs { get; set; } = 1500;

    /// <summary>How often (ms) to evaluate tag timeouts.</summary>
    public int EvictionIntervalMs { get; set; } = 250;

    /// <summary>How long (ms) to wait between reconnect attempts if a WebSocket drops.</summary>
    public int ReconnectDelayMs { get; set; } = 2000;

    /// <summary>
    /// Debounce window (ms) between the last table-state change and starting the analysis.
    /// Any new change within the window resets the timer, so a burst of card placements
    /// (e.g. dealing both hole cards or a full flop) results in exactly one recalculation.
    /// </summary>
    public int AnalysisDebounceMs { get; set; } = 400;

    /// <summary>
    /// If the table had an active hand and then goes fully empty (no community, hole, or muck
    /// cards) for this many milliseconds, automatically fire <c>NewHand</c>.
    /// </summary>
    public int IdleHandResetMs { get; set; } = 5000;

    /// <summary>
    /// If a seat that was dealt in loses its cards from the seat antenna for this many
    /// milliseconds, the player is auto-folded and their preserved hole cards are moved
    /// to the muck. Covers the case where a player picks up their cards or the dealer
    /// forgets to muck.
    /// </summary>
    public int MissingCardsFoldMs { get; set; } = 10000;

    public List<DeviceConfig> Devices { get; set; } = [];
}

public class DeviceConfig
{
    /// <summary>WebSocket URL of this Pepper reader, e.g. "ws://10.0.0.121/wscomm.cgi".</summary>
    public string WebSocketUrl { get; set; } = string.Empty;

    /// <summary>Optional friendly name shown in the UI. Defaults to the WebSocket host.</summary>
    public string? Name { get; set; }

    public List<AntennaConfig> Antennas { get; set; } = [];
}

public class AntennaConfig
{
    public int AntennaIndex { get; set; }
    public AntennaFunction Function { get; set; }
    /// <summary>Seat number (1-9) when Function is PlayerSeat.</summary>
    public int? SeatNumber { get; set; }
}

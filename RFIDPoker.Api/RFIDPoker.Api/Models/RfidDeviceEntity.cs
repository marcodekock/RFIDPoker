namespace RFIDPoker.Api.Models;

/// <summary>
/// Persisted MUX/reader configuration. Replaces the static
/// <c>Rfid:Devices</c> section from appsettings so operators can
/// add/edit MUXes at runtime.
/// </summary>
public class RfidDeviceEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WebSocketUrl { get; set; } = string.Empty;

    public List<RfidAntennaEntity> Antennas { get; set; } = [];
}

/// <summary>
/// Persisted antenna configuration for a MUX. A MUX supports at most
/// 8 antennas; enforced at API + UI layer.
/// </summary>
public class RfidAntennaEntity
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public RfidDeviceEntity? Device { get; set; }

    /// <summary>1-based antenna port index on the MUX (1..8).</summary>
    public int AntennaIndex { get; set; }

    public AntennaFunction Function { get; set; }

    /// <summary>Seat number (1..9) when <see cref="Function"/> is <c>PlayerSeat</c>.</summary>
    public int? SeatNumber { get; set; }
}

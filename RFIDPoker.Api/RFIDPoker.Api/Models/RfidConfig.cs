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

    public List<MuxConfig> Muxes { get; set; } = [];
    public int PollingIntervalMs { get; set; } = 100;
}

public class MuxConfig
{
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 115200;
    public List<AntennaConfig> Antennas { get; set; } = [];
}

public class AntennaConfig
{
    public int AntennaIndex { get; set; }
    public AntennaFunction Function { get; set; }
    /// <summary>Seat number (1-9) when Function is PlayerSeat.</summary>
    public int? SeatNumber { get; set; }
}

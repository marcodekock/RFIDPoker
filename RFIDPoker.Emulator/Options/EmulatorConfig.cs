namespace RFIDPoker.Emulator.Options;

/// <summary>
/// Configuration for the Pepper-reader emulator. Bound from the "Emulator" section
/// of appsettings.json.
/// </summary>
public class EmulatorConfig
{
    public const string SectionName = "Emulator";

    /// <summary>Number of players to deal hole cards to each hand (1-9).</summary>
    public int PlayerCount { get; set; } = 3;

    /// <summary>Base URL of the RFIDPoker.Api. Used to seed tag->card mappings on startup.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>If true, POSTs a mapping for every card in the deck at startup.</summary>
    public bool SeedMappingsOnStartup { get; set; } = true;

    /// <summary>Username used to obtain a JWT from the API before seeding mappings.</summary>
    public string? ApiUsername { get; set; }

    /// <summary>Password used to obtain a JWT from the API before seeding mappings.</summary>
    public string? ApiPassword { get; set; }

    /// <summary>Prefix for synthesized tag ids (e.g. "EMU-AS" for Ace of Spades).</summary>
    public string TagPrefix { get; set; } = "EMU-";

    /// <summary>
    /// How often (ms) to re-emit each currently-present tag. The API considers a tag
    /// removed if it hasn't been seen for RfidConfig.TagTimeoutMs (default 1500 ms),
    /// so this must be well under that.
    /// </summary>
    public int TagRepeatIntervalMs { get; set; } = 300;

    /// <summary>Wait before starting to deal hole cards each hand.</summary>
    public int PreDealDelayMs { get; set; } = 1500;

    /// <summary>Delay between placing individual cards (hole and community).</summary>
    public int BetweenCardDealMs { get; set; } = 350;

    /// <summary>How long to hold pre-flop before dealing the flop.</summary>
    public int PreFlopHoldMs { get; set; } = 6000;

    /// <summary>How long to hold the flop before dealing the turn.</summary>
    public int FlopHoldMs { get; set; } = 6000;

    /// <summary>How long to hold the turn before dealing the river.</summary>
    public int TurnHoldMs { get; set; } = 5000;

    /// <summary>How long to hold the river before ending the hand.</summary>
    public int RiverHoldMs { get; set; } = 8000;

    /// <summary>Delay between hands after the table is cleared.</summary>
    public int BetweenHandsMs { get; set; } = 3000;

    /// <summary>First antenna index used for player seats. Seat 1 -> this index, seat 2 -> +1, etc.</summary>
    public int SeatAntennaStart { get; set; } = 1;

    /// <summary>Antenna indices to distribute the flop cards across.</summary>
    public List<int> FlopAntennas { get; set; } = [5, 6];

    /// <summary>Antenna index that reads the turn and river cards.</summary>
    public int TurnRiverAntenna { get; set; } = 7;

    /// <summary>Antenna index that reads mucked (folded) cards.</summary>
    public int MuckAntenna { get; set; } = 4;

    /// <summary>If true, keeps dealing hands in a loop. If false, runs one hand and stops.</summary>
    public bool AutoLoopHands { get; set; } = true;

    /// <summary>Probability (0..1) that a random active player is folded (mucked) mid-hand.</summary>
    public double RandomFoldChance { get; set; } = 0.0;
}

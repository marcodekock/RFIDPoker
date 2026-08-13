namespace RFIDPoker.Api.Models;

public class Player
{
    public int SeatNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Card> HoleCards { get; set; } = [];
    public bool IsFolded { get; set; }
    public bool IsDealer { get; set; }
}

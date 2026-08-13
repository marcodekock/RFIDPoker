namespace RFIDPoker.Api.Models;

public enum Suit
{
    Hearts,
    Diamonds,
    Clubs,
    Spades
}

public enum Rank
{
    Two = 2,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace
}

public record Card(Rank Rank, Suit Suit)
{
    public override string ToString() => $"{Rank} of {Suit}";
}

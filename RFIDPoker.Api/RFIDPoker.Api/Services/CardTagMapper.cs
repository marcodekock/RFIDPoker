using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface ICardTagMapper
{
    Card? GetCard(string tagId);
    string? GetTagId(Card card);
    void RegisterMapping(string tagId, Card card);
    IReadOnlyDictionary<string, Card> GetAllMappings();
}

/// <summary>
/// Maps RFID tag IDs to playing cards. Mappings can be loaded from configuration
/// or registered at runtime via a calibration process.
/// </summary>
public class CardTagMapper : ICardTagMapper
{
    private readonly Dictionary<string, Card> _tagToCard = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Card, string> _cardToTag = [];

    public CardTagMapper(IConfiguration configuration)
    {
        var section = configuration.GetSection("CardMappings");
        if (section.Exists())
        {
            foreach (var child in section.GetChildren())
            {
                var tagId = child.Key;
                var value = child.Value;
                if (TryParseCard(value, out var card))
                {
                    _tagToCard[tagId] = card;
                    _cardToTag[card] = tagId;
                }
            }
        }
    }

    public Card? GetCard(string tagId)
        => _tagToCard.TryGetValue(tagId, out var card) ? card : null;

    public string? GetTagId(Card card)
        => _cardToTag.TryGetValue(card, out var tagId) ? tagId : null;

    public void RegisterMapping(string tagId, Card card)
    {
        _tagToCard[tagId] = card;
        _cardToTag[card] = tagId;
    }

    public IReadOnlyDictionary<string, Card> GetAllMappings() => _tagToCard;

    private static bool TryParseCard(string? value, out Card card)
    {
        card = default!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Expected format: "Rank_Suit" e.g. "Ace_Spades", "Two_Hearts"
        var parts = value.Split('_');
        if (parts.Length != 2) return false;

        if (!Enum.TryParse<Rank>(parts[0], true, out var rank)) return false;
        if (!Enum.TryParse<Suit>(parts[1], true, out var suit)) return false;

        card = new Card(rank, suit);
        return true;
    }
}

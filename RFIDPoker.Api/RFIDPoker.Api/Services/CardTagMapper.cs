using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface ICardTagMapper
{
    Card? GetCard(string tagId);
    string? GetTagId(Card card);
    void RegisterMapping(string tagId, Card card);
    bool DeleteMapping(string tagId);
    IReadOnlyDictionary<string, Card> GetAllMappings();
}

/// <summary>
/// Maps RFID tag IDs to playing cards. Mappings are persisted in a SQLite database via
/// <see cref="AppDbContext"/> and cached in memory for fast lookup on the RFID hot path.
/// Optional seed entries from configuration ("CardMappings" section) are inserted on first
/// startup only if the DB is empty.
/// </summary>
public class CardTagMapper : ICardTagMapper
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CardTagMapper> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, Card> _tagToCard = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Card, string> _cardToTag = [];

    public CardTagMapper(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CardTagMapper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        SeedFromConfiguration(db, configuration);

        foreach (var entity in db.CardMappings.AsNoTracking().ToList())
        {
            var card = new Card(entity.Rank, entity.Suit);
            _tagToCard[entity.TagId] = card;
            _cardToTag[card] = entity.TagId;
        }

        _logger.LogInformation("Loaded {Count} card mapping(s) from database.", _tagToCard.Count);
    }

    public Card? GetCard(string tagId)
    {
        lock (_sync)
        {
            return _tagToCard.TryGetValue(tagId, out var card) ? card : null;
        }
    }

    public string? GetTagId(Card card)
    {
        lock (_sync)
        {
            return _cardToTag.TryGetValue(card, out var tagId) ? tagId : null;
        }
    }

    public void RegisterMapping(string tagId, Card card)
    {
        lock (_sync)
        {
            // If this card was previously bound to a different tag, drop the old binding.
            if (_cardToTag.TryGetValue(card, out var existingTag) &&
                !string.Equals(existingTag, tagId, StringComparison.OrdinalIgnoreCase))
            {
                _tagToCard.Remove(existingTag);
            }

            _tagToCard[tagId] = card;
            _cardToTag[card] = tagId;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Remove any prior row for this card (different tag) then upsert this tag.
        var stale = db.CardMappings
            .Where(m => m.Rank == card.Rank && m.Suit == card.Suit && m.TagId != tagId)
            .ToList();
        if (stale.Count > 0) db.CardMappings.RemoveRange(stale);

        var existing = db.CardMappings.FirstOrDefault(m => m.TagId == tagId);
        if (existing is null)
        {
            db.CardMappings.Add(new CardMappingEntity { TagId = tagId, Rank = card.Rank, Suit = card.Suit });
        }
        else
        {
            existing.Rank = card.Rank;
            existing.Suit = card.Suit;
        }

        db.SaveChanges();
    }

    public bool DeleteMapping(string tagId)
    {
        lock (_sync)
        {
            if (!_tagToCard.TryGetValue(tagId, out var card)) return false;
            _tagToCard.Remove(tagId);
            _cardToTag.Remove(card);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.CardMappings.FirstOrDefault(m => m.TagId == tagId);
        if (row is null) return true;

        db.CardMappings.Remove(row);
        db.SaveChanges();
        return true;
    }

    public IReadOnlyDictionary<string, Card> GetAllMappings()
    {
        lock (_sync)
        {
            return _tagToCard.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SeedFromConfiguration(AppDbContext db, IConfiguration configuration)
    {
        if (db.CardMappings.Any()) return;

        var section = configuration.GetSection("CardMappings");
        if (!section.Exists()) return;

        var seeded = 0;
        foreach (var child in section.GetChildren())
        {
            var tagId = child.Key;
            if (!TryParseCard(child.Value, out var card)) continue;

            db.CardMappings.Add(new CardMappingEntity { TagId = tagId, Rank = card.Rank, Suit = card.Suit });
            seeded++;
        }

        if (seeded > 0)
        {
            db.SaveChanges();
            _logger.LogInformation("Seeded {Count} card mapping(s) from configuration.", seeded);
        }
    }

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

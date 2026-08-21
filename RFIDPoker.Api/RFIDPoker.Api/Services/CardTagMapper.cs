using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

public interface ICardTagMapper
{
    Card? GetCard(string tagId);
    string? GetTagId(Card card);

    /// <summary>Returns the deck id of the deck a tag currently maps to, or null if unknown.</summary>
    int? GetDeckId(string tagId);

    void RegisterMapping(int deckId, string tagId, Card card);
    bool DeleteMapping(int deckId, string tagId);
    IReadOnlyList<CardMappingSnapshot> GetAllMappings();

    /// <summary>Re-read mappings for all currently-enabled decks.</summary>
    void Reload();
}

public record CardMappingSnapshot(int DeckId, string DeckName, string TagId, Rank Rank, Suit Suit);

/// <summary>
/// Maps RFID tag IDs to playing cards. The runtime lookup is the UNION of every
/// deck flagged <see cref="DeckEntity.IsEnabled"/>. If the same tag exists in
/// multiple enabled decks the highest deck id wins (deterministic and predictable).
/// Writes always target a specific deck id chosen by the caller.
/// </summary>
public class CardTagMapper : ICardTagMapper
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CardTagMapper> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, Card> _tagToCard = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Card, string> _cardToTag = [];
    private readonly Dictionary<string, int> _tagToDeck = new(StringComparer.OrdinalIgnoreCase);

    public CardTagMapper(IServiceScopeFactory scopeFactory, ILogger<CardTagMapper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        EnsureDefaultDeck(db);
        LoadMappings(db);
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

    public int? GetDeckId(string tagId)
    {
        lock (_sync)
        {
            return _tagToDeck.TryGetValue(tagId, out var deckId) ? deckId : null;
        }
    }

    public void RegisterMapping(int deckId, string tagId, Card card)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Decks.Any(d => d.Id == deckId))
            throw new InvalidOperationException($"Deck {deckId} not found.");

        // Remove any prior row within this deck for this card (different tag) so the
        // (rank, suit) slot is unique per deck.
        var stale = db.CardMappings
            .Where(m => m.DeckId == deckId && m.Rank == card.Rank && m.Suit == card.Suit && m.TagId != tagId)
            .ToList();
        if (stale.Count > 0) db.CardMappings.RemoveRange(stale);

        var existing = db.CardMappings.FirstOrDefault(m => m.DeckId == deckId && m.TagId == tagId);
        if (existing is null)
        {
            db.CardMappings.Add(new CardMappingEntity
            {
                DeckId = deckId,
                TagId = tagId,
                Rank = card.Rank,
                Suit = card.Suit
            });
        }
        else
        {
            existing.Rank = card.Rank;
            existing.Suit = card.Suit;
        }

        db.SaveChanges();
        LoadMappings(db);
    }

    public bool DeleteMapping(int deckId, string tagId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.CardMappings.FirstOrDefault(m => m.DeckId == deckId && m.TagId == tagId);
        if (row is null) return false;

        db.CardMappings.Remove(row);
        db.SaveChanges();
        LoadMappings(db);
        return true;
    }

    public IReadOnlyList<CardMappingSnapshot> GetAllMappings()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.CardMappings
            .AsNoTracking()
            .Include(m => m.Deck)
            .OrderBy(m => m.DeckId)
            .ThenBy(m => m.Suit)
            .ThenBy(m => m.Rank)
            .Select(m => new CardMappingSnapshot(m.DeckId, m.Deck!.Name, m.TagId, m.Rank, m.Suit))
            .ToList();
    }

    public void Reload()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        LoadMappings(db);
    }

    private void LoadMappings(AppDbContext db)
    {
        // Union of every enabled deck's mappings. Higher DeckId wins on tag collisions
        // so the ordering here matters: iterate low-to-high, later writes overwrite.
        var rows = db.CardMappings
            .AsNoTracking()
            .Where(m => m.Deck!.IsEnabled)
            .OrderBy(m => m.DeckId)
            .ToList();

        lock (_sync)
        {
            _tagToCard.Clear();
            _cardToTag.Clear();
            _tagToDeck.Clear();
            foreach (var entity in rows)
            {
                var card = new Card(entity.Rank, entity.Suit);
                _tagToCard[entity.TagId] = card;
                _cardToTag[card] = entity.TagId;
                _tagToDeck[entity.TagId] = entity.DeckId;
            }
        }

        _logger.LogInformation("Loaded {Count} mapping(s) across enabled deck(s).", rows.Count);
    }

    private static void EnsureDefaultDeck(AppDbContext db)
    {
        if (db.Decks.Any()) return;
        db.Decks.Add(new DeckEntity { Name = "Default Deck", IsEnabled = true });
        db.SaveChanges();
    }
}

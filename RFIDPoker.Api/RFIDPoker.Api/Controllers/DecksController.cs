using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

/// <summary>
/// CRUD for named card decks. Any combination of decks can be enabled; the
/// runtime tag lookup is the union of every enabled deck's mappings.
/// </summary>
[ApiController]
[Route("api/decks")]
[Authorize(Policy = AuthPolicies.RequireUser)]
public class DecksController(
    AppDbContext db,
    ICardTagMapper mapper) : ControllerBase
{
    public record DeckDto(int Id, string Name, int MappingCount, bool IsEnabled);
    public record UpsertDeckRequest(string Name);
    public record SetEnabledRequest(bool IsEnabled);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeckDto>>> GetAll(CancellationToken ct)
    {
        var decks = await db.Decks
            .OrderBy(d => d.Id)
            .Select(d => new DeckDto(d.Id, d.Name, d.Mappings.Count, d.IsEnabled))
            .ToListAsync(ct);
        return Ok(decks);
    }

    [HttpPost]
    public async Task<ActionResult<DeckDto>> Create([FromBody] UpsertDeckRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        if (await db.Decks.AnyAsync(d => d.Name == name, ct))
            return Conflict($"A deck named '{name}' already exists.");

        var deck = new DeckEntity { Name = name, IsEnabled = true };
        db.Decks.Add(deck);
        await db.SaveChangesAsync(ct);

        mapper.Reload();
        return Ok(new DeckDto(deck.Id, deck.Name, 0, deck.IsEnabled));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DeckDto>> Rename(int id, [FromBody] UpsertDeckRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Name is required.");

        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (deck is null) return NotFound();

        if (await db.Decks.AnyAsync(d => d.Id != id && d.Name == name, ct))
            return Conflict($"A deck named '{name}' already exists.");

        deck.Name = name;
        await db.SaveChangesAsync(ct);

        var count = await db.CardMappings.CountAsync(m => m.DeckId == id, ct);
        return Ok(new DeckDto(deck.Id, deck.Name, count, deck.IsEnabled));
    }

    [HttpPut("{id:int}/enabled")]
    public async Task<IActionResult> SetEnabled(int id, [FromBody] SetEnabledRequest request, CancellationToken ct)
    {
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (deck is null) return NotFound();

        deck.IsEnabled = request.IsEnabled;
        await db.SaveChangesAsync(ct);
        mapper.Reload();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deck = await db.Decks.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (deck is null) return NotFound();

        if (await db.Decks.CountAsync(ct) <= 1)
            return BadRequest("Cannot delete the last remaining deck.");

        db.Decks.Remove(deck);
        await db.SaveChangesAsync(ct);
        mapper.Reload();
        return NoContent();
    }
}

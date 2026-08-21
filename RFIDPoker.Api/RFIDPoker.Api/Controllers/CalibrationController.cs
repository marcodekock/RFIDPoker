using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.RequireUser)]
public class CalibrationController(
    ICardTagMapper cardMapper,
    IOptions<RfidConfig> rfidConfig,
    IRfidReaderService rfidReader) : ControllerBase
{
    [HttpGet("mappings")]
    public ActionResult<List<CardMappingDto>> GetMappings()
    {
        var mappings = cardMapper.GetAllMappings()
            .Select(m => new CardMappingDto(m.DeckId, m.DeckName, m.TagId, (int)m.Rank, (int)m.Suit))
            .ToList();
        return Ok(mappings);
    }

    [HttpPost("mappings")]
    public IActionResult RegisterMapping([FromBody] RegisterMappingRequest request)
    {
        if (!Enum.IsDefined(typeof(Rank), request.Rank) || !Enum.IsDefined(typeof(Suit), request.Suit))
            return BadRequest("Invalid rank or suit value.");
        if (request.DeckId <= 0)
            return BadRequest("DeckId is required.");

        var card = new Card((Rank)request.Rank, (Suit)request.Suit);
        try
        {
            cardMapper.RegisterMapping(request.DeckId, request.TagId, card);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        return Ok();
    }

    [HttpDelete("mappings")]
    public IActionResult DeleteMapping([FromBody] DeleteMappingRequest request)
    {
        var removed = cardMapper.DeleteMapping(request.DeckId, request.TagId);
        return removed ? NoContent() : NotFound();
    }

    [HttpGet("readings")]
    public ActionResult<List<AntennaReadingDto>> GetAntennaReadings()
        => Ok(rfidReader.GetReadingsSnapshot());
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CalibrationController(
    ICardTagMapper cardMapper,
    IOptions<RfidConfig> rfidConfig,
    IRfidReaderService rfidReader) : ControllerBase
{
    [HttpGet("mappings")]
    public ActionResult<List<CardMappingDto>> GetMappings()
    {
        var mappings = cardMapper.GetAllMappings()
            .Select(kv => new CardMappingDto(kv.Key, (int)kv.Value.Rank, (int)kv.Value.Suit))
            .ToList();
        return Ok(mappings);
    }

    [HttpPost("mappings")]
    public IActionResult RegisterMapping([FromBody] RegisterMappingRequest request)
    {
        if (!Enum.IsDefined(typeof(Rank), request.Rank) || !Enum.IsDefined(typeof(Suit), request.Suit))
            return BadRequest("Invalid rank or suit value.");

        var card = new Card((Rank)request.Rank, (Suit)request.Suit);
        cardMapper.RegisterMapping(request.TagId, card);
        return Ok();
    }

    [HttpDelete("mappings/{tagId}")]
    public IActionResult DeleteMapping(string tagId)
    {
        var removed = cardMapper.DeleteMapping(tagId);
        return removed ? NoContent() : NotFound();
    }

    [HttpGet("readings")]
    public ActionResult<List<AntennaReadingDto>> GetAntennaReadings()
        => Ok(rfidReader.GetReadingsSnapshot());
}

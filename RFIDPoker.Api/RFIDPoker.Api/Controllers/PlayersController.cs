using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlayersController(ITableStateManager tableState) : ControllerBase
{
    /// <summary>Returns the current name and chip count for every known seat.</summary>
    [HttpGet]
    public ActionResult<List<PlayerNameDto>> GetAll()
    {
        var names = tableState.Players
            .OrderBy(p => p.SeatNumber)
            .Select(p => new PlayerNameDto(p.SeatNumber, p.Name, p.ChipCount))
            .ToList();
        return Ok(names);
    }

    /// <summary>Sets (or creates) the name for a single seat.</summary>
    [HttpPut("{seatNumber:int}/name")]
    public ActionResult<PlayerNameDto> SetName(int seatNumber, [FromBody] SetPlayerNameRequest request)
    {
        if (seatNumber < 1 || seatNumber > 9)
            return BadRequest("SeatNumber must be between 1 and 9.");
        if (request is null)
            return BadRequest("Request body is required.");

        tableState.SetPlayerName(seatNumber, request.Name ?? string.Empty);

        var player = tableState.Players.First(p => p.SeatNumber == seatNumber);
        return Ok(new PlayerNameDto(player.SeatNumber, player.Name, player.ChipCount));
    }

    /// <summary>
    /// Bulk-sets names and chip counts for multiple seats in one call.
    /// Chip count is always applied from the payload: provide a value to set it,
    /// omit it (or send null) to clear it. This lets you re-post the same list
    /// without chip counts to reset everyone's chips.
    /// </summary>
    [HttpPut]
    public ActionResult<List<PlayerNameDto>> SetNames([FromBody] SetPlayerNamesRequest request)
    {
        if (request?.Players is null || request.Players.Count == 0)
            return BadRequest("At least one player is required.");

        foreach (var p in request.Players)
        {
            if (p.SeatNumber < 1 || p.SeatNumber > 9)
                return BadRequest($"Invalid seat number: {p.SeatNumber}. Must be 1-9.");
            if (p.ChipCount is < 0)
                return BadRequest($"ChipCount for seat {p.SeatNumber} cannot be negative.");
        }

        var nameMap = request.Players.ToDictionary(p => p.SeatNumber, p => p.Name ?? string.Empty);
        tableState.SetPlayerNames(nameMap);

        foreach (var p in request.Players)
        {
            tableState.SetPlayerChipCount(p.SeatNumber, p.ChipCount);
        }

        var updatedSeats = request.Players.Select(p => p.SeatNumber).ToHashSet();
        var result = tableState.Players
            .Where(pl => updatedSeats.Contains(pl.SeatNumber))
            .OrderBy(pl => pl.SeatNumber)
            .Select(pl => new PlayerNameDto(pl.SeatNumber, pl.Name, pl.ChipCount))
            .ToList();
        return Ok(result);
    }

    /// <summary>Sets (or clears) the chip count for a seat. Null clears it.</summary>
    [HttpPut("{seatNumber:int}/chips")]
    public IActionResult SetChipCount(int seatNumber, [FromBody] SetPlayerChipCountRequest request)
    {
        if (seatNumber < 1 || seatNumber > 9)
            return BadRequest("SeatNumber must be between 1 and 9.");
        if (request is null)
            return BadRequest("Request body is required.");
        if (request.ChipCount is < 0)
            return BadRequest("ChipCount cannot be negative.");

        tableState.SetPlayerChipCount(seatNumber, request.ChipCount);
        return NoContent();
    }
}

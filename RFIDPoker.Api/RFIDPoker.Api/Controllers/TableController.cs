using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.RequireUser)]
public class TableController(ITableStateManager tableState) : ControllerBase
{
    /// <summary>Gets the current blinds string, or null if unset.</summary>
    [HttpGet("blinds")]
    public ActionResult<object> GetBlinds() => Ok(new { blinds = tableState.Blinds });

    /// <summary>
    /// Sets the blinds display string, e.g. "2000/4000". Pass null or an empty string
    /// to clear it (in which case the UI will hide the blinds display).
    /// </summary>
    [HttpPut("blinds")]
    public IActionResult SetBlinds([FromBody] SetBlindsRequest request)
    {
        tableState.SetBlinds(request?.Blinds);
        return NoContent();
    }

    /// <summary>Clears the blinds display.</summary>
    [HttpDelete("blinds")]
    public IActionResult ClearBlinds()
    {
        tableState.SetBlinds(null);
        return NoContent();
    }
}

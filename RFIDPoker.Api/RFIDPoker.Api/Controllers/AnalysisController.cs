using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.RequireUser)]
public class AnalysisController(IPokerAnalysisEngine analysisEngine) : ControllerBase
{
    [HttpGet("current")]
    public ActionResult<AnalysisResultDto> GetCurrent()
    {
        var result = analysisEngine.GetLatestResult();
        if (result is null)
            return NotFound("No analysis available yet.");

        return Ok(result);
    }
}

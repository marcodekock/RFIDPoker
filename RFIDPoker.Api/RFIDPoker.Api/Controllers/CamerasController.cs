using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Dtos;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class CamerasController(
    ICameraRepository repo,
    ICameraDirector director) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CameraDto>>> GetAll(CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CameraDto>> Create([FromBody] CreateCameraRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ObsSceneName))
            return BadRequest("Name and ObsSceneName are required.");

        var created = await repo.AddAsync(new Camera
        {
            Name = request.Name.Trim(),
            ObsSceneName = request.ObsSceneName.Trim(),
            Role = request.Role,
            SortOrder = request.SortOrder,
            Enabled = request.Enabled
        }, ct);

        return CreatedAtAction(nameof(GetAll), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCameraRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ObsSceneName))
            return BadRequest("Name and ObsSceneName are required.");

        var ok = await repo.UpdateAsync(new Camera
        {
            Id = id,
            Name = request.Name.Trim(),
            ObsSceneName = request.ObsSceneName.Trim(),
            Role = request.Role,
            SortOrder = request.SortOrder,
            Enabled = request.Enabled
        }, ct);

        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ok = await repo.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("status")]
    public ActionResult<CameraStatusDto> GetStatus()
    {
        var s = director.GetStatus();
        return Ok(new CameraStatusDto(s.Enabled, s.Connected, s.CurrentScene, s.DesiredScene, s.HandInProgress));
    }

    private static CameraDto ToDto(Camera c)
        => new(c.Id, c.Name, c.ObsSceneName, c.Role, c.SortOrder, c.Enabled);
}

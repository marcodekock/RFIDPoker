using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

namespace RFIDPoker.Api.Controllers;

/// <summary>
/// Admin CRUD for the dynamic RFID MUX/antenna layout. Replaces the
/// static <c>Rfid:Devices</c> section from appsettings so operators can
/// re-map antennas on the fly when moving the rig between tables.
/// </summary>
[ApiController]
[Route("api/rfid/devices")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
public class RfidDevicesController(IRfidDeviceStore store) : ControllerBase
{
    // A MUX physically has 8 antenna ports.
    private const int MaxAntennasPerDevice = 8;

    public record AntennaDto(int AntennaIndex, string Function, int? SeatNumber);
    public record DeviceDto(string Name, string WebSocketUrl, List<AntennaDto> Antennas);

    [HttpGet]
    public ActionResult<IReadOnlyList<DeviceDto>> Get()
        => Ok(store.Devices.Select(ToDto).ToList());

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> Replace(
        [FromBody] List<DeviceDto> devices, CancellationToken ct)
    {
        if (devices is null) return BadRequest("Body required.");

        var errors = Validate(devices);
        if (errors.Count > 0) return BadRequest(new { errors });

        await store.ReplaceAsync(devices.Select(FromDto), ct);
        return Ok(store.Devices.Select(ToDto).ToList());
    }

    private static List<string> Validate(List<DeviceDto> devices)
    {
        var errors = new List<string>();
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            var label = string.IsNullOrWhiteSpace(d.Name) ? $"MUX #{i + 1}" : d.Name;

            if (string.IsNullOrWhiteSpace(d.WebSocketUrl))
                errors.Add($"{label}: WebSocket URL is required.");
            else if (!Uri.TryCreate(d.WebSocketUrl, UriKind.Absolute, out _))
                errors.Add($"{label}: WebSocket URL '{d.WebSocketUrl}' is not a valid absolute URI.");

            if (d.Antennas.Count > MaxAntennasPerDevice)
                errors.Add($"{label}: Too many antennas ({d.Antennas.Count}); max is {MaxAntennasPerDevice}.");

            var indexes = new HashSet<int>();
            foreach (var a in d.Antennas)
            {
                if (a.AntennaIndex is < 1 or > MaxAntennasPerDevice)
                    errors.Add($"{label}: Antenna index {a.AntennaIndex} out of range (1..{MaxAntennasPerDevice}).");
                if (!indexes.Add(a.AntennaIndex))
                    errors.Add($"{label}: Duplicate antenna index {a.AntennaIndex}.");

                if (!Enum.TryParse<AntennaFunction>(a.Function, ignoreCase: true, out var fn))
                {
                    errors.Add($"{label}: Unknown function '{a.Function}' on antenna {a.AntennaIndex}.");
                    continue;
                }
                if (fn == AntennaFunction.PlayerSeat)
                {
                    if (a.SeatNumber is null or < 1 or > 9)
                        errors.Add($"{label}: PlayerSeat antenna {a.AntennaIndex} requires SeatNumber 1..9.");
                }
            }
        }
        return errors;
    }

    private static DeviceDto ToDto(DeviceConfig d) => new(
        d.Name ?? d.WebSocketUrl,
        d.WebSocketUrl,
        d.Antennas.Select(a => new AntennaDto(a.AntennaIndex, a.Function.ToString(), a.SeatNumber)).ToList());

    private static DeviceConfig FromDto(DeviceDto d) => new()
    {
        Name = string.IsNullOrWhiteSpace(d.Name) ? null : d.Name.Trim(),
        WebSocketUrl = d.WebSocketUrl.Trim(),
        Antennas = d.Antennas
            .OrderBy(a => a.AntennaIndex)
            .Select(a => new AntennaConfig
            {
                AntennaIndex = a.AntennaIndex,
                Function = Enum.Parse<AntennaFunction>(a.Function, ignoreCase: true),
                SeatNumber = Enum.Parse<AntennaFunction>(a.Function, ignoreCase: true) == AntennaFunction.PlayerSeat
                    ? a.SeatNumber
                    : null
            })
            .ToList()
    };
}
